using System.Numerics;
using EasyDesktopLyrics.Infrastructure;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// 频谱引擎：优先 WASAPI 环回采集真实音频做 FFT；采集不可用时自动回退到
/// 播放状态驱动的模拟频谱。对外暴露 N 个归一化频带（0~1，N 可配置）。
/// 真实路径：汉明窗 → FFT → 频段聚合（对数分布 + 频段补偿曲线）→ 平滑。
/// </summary>
public sealed class SpectrumEngine : IDisposable
{
    private const int FftSize = 1024;
    private const int MinFreq = 40;
    private const int MaxFreq = 16000;
    private const int DefaultBandCount = 32;

    private readonly object _lock = new();
    private readonly List<float> _samples = new(FftSize * 2);
    private readonly Complex[] _fftBuf = new Complex[FftSize];
    private readonly double[] _hamming = new double[FftSize];
    private float[] _target = new float[DefaultBandCount];
    private float[] _smoothed = new float[DefaultBandCount];
    private readonly WasapiLoopbackCapture? _capture;
    private bool _real;
    private bool _disposed;
    private int _sampleRate = 48000;

    // 模拟频谱状态
    private double _simTime;
    private double _playingLevel;
    private readonly System.Diagnostics.Stopwatch _simClock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastSimT;

    public SpectrumEngine()
    {
        for (var i = 0; i < FftSize; i++)
            _hamming[i] = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
        _capture = new WasapiLoopbackCapture();
        _capture.DataAvailable += OnData;
    }

    /// <summary>true = 真实环回频谱；false = 模拟回退。</summary>
    public bool IsReal => _real;

    public int Count => _smoothed.Length;

    public event Action<bool>? SourceChanged;

    /// <summary>异步启动采集；成功则进入真实模式。调用方应 await 后判断 IsReal。</summary>
    public async Task<bool> StartAsync()
    {
        try
        {
            if (_capture == null)
                return false;
            var ok = await _capture.StartAsync().ConfigureAwait(false);
            _real = ok;
            Log.Info($"spectrum source: {(ok ? "wasapi-loopback" : "simulated")} {(ok ? "" : _capture.Error?.Message ?? "")}");
            if (!ok)
                _capture.DataAvailable -= OnData;
            SourceChanged?.Invoke(_real);
            return _real;
        }
        catch (Exception ex)
        {
            Log.Error("spectrum start", ex);
            _real = false;
            SourceChanged?.Invoke(false);
            return false;
        }
    }

    /// <summary>播放状态（模拟频谱以播放状态驱动振幅，暂停时衰减到 0）。</summary>
    public void SetPlaying(bool playing)
    {
        lock (_lock)
        {
            if (playing)
                _playingLevel += (1.0 - _playingLevel) * 0.2;
            else
                _playingLevel *= 0.8;
        }
    }

    /// <summary>平滑设置（1–10，越大越柔和）。</summary>
    public void SetSmoothing(int smoothing)
    {
        _smoothing = Math.Clamp(smoothing, 1, 10);
    }

    private int _smoothing = 3;

    /// <summary>设置频带数量（16–128），动态调整输出数组。</summary>
    public void SetBandCount(int count)
    {
        count = Math.Clamp(count, 16, 128);
        lock (_lock)
        {
            if (count == _smoothed.Length)
                return;
            Array.Resize(ref _target, count);
            Array.Resize(ref _smoothed, count);
        }
    }

    /// <summary>当前频带快照（长度 = Count，0~1，内部复用数组）。</summary>
    public float[] GetBands()
    {
        if (_real)
            return ComputeRealBands();
        return ComputeSimulatedBands();
    }

    // ---------- 真实频谱 ----------

    private void OnData(float[] mono)
    {
        lock (_lock)
        {
            if (_disposed || !_real)
                return;
            _samples.AddRange(mono);
            if (_samples.Count > FftSize * 4)
                _samples.RemoveRange(0, _samples.Count - FftSize * 2);
        }
    }

    private float[] ComputeRealBands()
    {
        var count = _smoothed.Length;
        lock (_lock)
        {
            if (_samples.Count < FftSize)
            {
                DecayToZero(count);
                return _smoothed;
            }

            var start = _samples.Count - FftSize;
            for (var i = 0; i < FftSize; i++)
                _fftBuf[i] = new Complex(_samples[start + i] * _hamming[i], 0);
        }

        FftSharp.FFT.Forward(_fftBuf);
        FillTargetFromMagnitudes(count);
        return SmoothAndReturn(count);
    }

    /// <summary>对数频段聚合 + 频段补偿曲线（低频补强、高频适度滚降，视觉更平衡）。</summary>
    private void FillTargetFromMagnitudes(int count)
    {
        var nyquist = _sampleRate / 2.0;
        for (var b = 0; b < count; b++)
        {
            var f0 = MinFreq * Math.Pow((double)MaxFreq / MinFreq, (double)b / count);
            var f1 = MinFreq * Math.Pow((double)MaxFreq / MinFreq, (double)(b + 1) / count);
            var k0 = Math.Max(2, (int)(f0 / nyquist * (FftSize / 2)));
            var k1 = Math.Min(FftSize / 2, (int)(f1 / nyquist * (FftSize / 2)) + 1);
            if (k1 <= k0)
            {
                _target[b] = 0;
                continue;
            }
            double sum = 0;
            for (var k = k0; k < k1; k++)
                sum += _fftBuf[k].Magnitude;
            var norm = sum / (k1 - k0) / (FftSize / 2.0);
            var gain = CompensationGain(f0, f1);
            _target[b] = (float)Math.Clamp(norm * 2.0 * gain, 0, 1);
        }
    }

    /// <summary>频段补偿增益曲线（线性插值）：20Hz 起低频补强，高频渐进滚降。</summary>
    private static double CompensationGain(double fLo, double fHi)
    {
        ReadOnlySpan<double> freqs = [20, 50, 100, 200, 500, 1000, 2000, 4000, 8000, 16000, 20000];
        ReadOnlySpan<double> gains = [1.6, 1.5, 1.3, 1.1, 1.0, 1.0, 0.9, 0.75, 0.6, 0.5, 0.45];
        var f = (fLo + fHi) / 2;
        if (f <= freqs[0])
            return gains[0];
        if (f >= freqs[^1])
            return gains[^1];
        for (var i = 0; i < freqs.Length - 1; i++)
        {
            if (f <= freqs[i + 1])
            {
                var t = (f - freqs[i]) / (freqs[i + 1] - freqs[i]);
                return gains[i] + (gains[i + 1] - gains[i]) * t;
            }
        }
        return 1.0;
    }

    // ---------- 模拟频谱 ----------

    private float[] ComputeSimulatedBands()
    {
        var count = _smoothed.Length;
        lock (_lock)
        {
            // 按真实流逝时间推进（与渲染帧率解耦，动画速度恒定）
            var now = _simClock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(now - _lastSimT, 0, 0.1);
            _lastSimT = now;
            _simTime += dt;
            for (var b = 0; b < count; b++)
            {
                var x = b / (double)count;
                var t = _simTime;
                var v =
                    0.5 + 0.5 * Math.Sin(x * 2.3 + t * 1.3)
                    + 0.5 + 0.5 * Math.Sin(x * 7.1 - t * 0.9)
                    + 0.5 + 0.5 * Math.Sin(x * 3.7 + t * 2.1 + 1.7);
                v = v / 3.0;
                // 低频更饱满的高频衰减包络
                var envelope = 0.35 + 0.65 * Math.Pow(1 - x, 1.6);
                _target[b] = (float)(v * envelope * _playingLevel);
            }
        }
        return SmoothAndReturn(count);
    }

    // ---------- 平滑与输出 ----------

    private float[] SmoothAndReturn(int count)
    {
        var attack = 1.0f / (1 + _smoothing);
        var decay = (float)Math.Pow(0.985, _smoothing * 2);
        for (var i = 0; i < count; i++)
        {
            // 非线性提升（^0.7）：小信号更明显，让频谱视觉上更饱满
            var t = (float)Math.Pow(Math.Clamp(_target[i], 0, 1), 0.7);
            var prev = _smoothed[i];
            _smoothed[i] = t > prev ? prev + (t - prev) * attack : prev * decay;
            if (_smoothed[i] < 0.001f)
                _smoothed[i] = 0;
        }
        return _smoothed;
    }

    private void DecayToZero(int count)
    {
        var decay = (float)Math.Pow(0.96, _smoothing * 2);
        for (var i = 0; i < count; i++)
            _smoothed[i] *= decay;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        if (_capture != null)
        {
            _capture.DataAvailable -= OnData;
            _capture.Dispose();
        }
    }
}
