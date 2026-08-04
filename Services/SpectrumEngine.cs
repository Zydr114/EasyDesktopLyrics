using System.Numerics;
using EasyDesktopLyrics.Infrastructure;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// 频谱引擎：优先 WASAPI 环回采集真实音频做双声道 FFT；采集不可用时自动回退到
/// 播放状态驱动的模拟频谱（伪立体声）。
/// 真实路径：时域自动增益 → 汉明窗 → L/R 各自 FFT → 对数频段聚合（+中高频补偿）→
/// 轻量 bass pump → 高频活性增强 → 峰值保持平滑。
/// 对外暴露一帧 L/R 频带快照（0~1）与 BassEnergy（0~1，供渲染呼吸缩放）。
/// </summary>
public sealed class SpectrumEngine : IDisposable
{
    private const int FftSize = 2048;
    private const int MinFreq = 40;
    private const int MaxFreq = 12000;
    private const int DefaultBandCount = 32;

    private readonly object _lock = new();
    private readonly List<float> _samples = new(FftSize * 4);
    private readonly Complex[] _fftBufL = new Complex[FftSize];
    private readonly Complex[] _fftBufR = new Complex[FftSize];
    private readonly double[] _hamming = new double[FftSize];
    private float[] _targetL = new float[DefaultBandCount];
    private float[] _targetR = new float[DefaultBandCount];
    private float[] _smoothedL = new float[DefaultBandCount];
    private float[] _smoothedR = new float[DefaultBandCount];
    private float[] _peakHoldL = new float[DefaultBandCount];
    private float[] _peakHoldR = new float[DefaultBandCount];
    private readonly WasapiLoopbackCapture? _capture;
    private bool _real;
    private bool _disposed;
    private int _sampleRate = 48000;

    // 模拟频谱状态
    private double _simTime;
    private double _playingLevel;
    private readonly System.Diagnostics.Stopwatch _simClock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastSimT;

    // 时域自动增益（BetterLyrics 风格）
    private float _maxDetectedVolume = 0.1f;
    private bool _isMono;
    private float _bassEnergy;

    public SpectrumEngine()
    {
        for (var i = 0; i < FftSize; i++)
            _hamming[i] = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
        _capture = new WasapiLoopbackCapture();
        _capture.DataAvailable += OnData;
    }

    /// <summary>true = 真实环回频谱；false = 模拟回退。</summary>
    public bool IsReal => _real;

    public int Count => _smoothedL.Length;

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
            if (count == _smoothedL.Length)
                return;
            Array.Resize(ref _targetL, count);
            Array.Resize(ref _targetR, count);
            Array.Resize(ref _smoothedL, count);
            Array.Resize(ref _smoothedR, count);
            Array.Resize(ref _peakHoldL, count);
            Array.Resize(ref _peakHoldR, count);
        }
    }

    /// <summary>当前帧 L/R 频带快照（长度 = Count，0~1）+ 低音能量。数组为内部复用，须当帧用完。</summary>
    public SpectrumFrame GetFrame()
    {
        if (_real)
            return ComputeRealFrame();
        return ComputeSimulatedFrame();
    }

    // ---------- 真实频谱 ----------

    private void OnData(float[] stereo)
    {
        lock (_lock)
        {
            if (_disposed || !_real)
                return;
            _samples.AddRange(stereo);
            if (_samples.Count > FftSize * 4)
                _samples.RemoveRange(0, _samples.Count - FftSize * 2);
        }
    }

    private SpectrumFrame ComputeRealFrame()
    {
        var count = _smoothedL.Length;
        lock (_lock)
        {
            if (_samples.Count < FftSize * 2)
            {
                DecayToZero(count);
                _bassEnergy = 0;
                return new SpectrumFrame(_smoothedL, _smoothedR, 0);
            }

            var start = _samples.Count - FftSize * 2;
            var gain = ComputeAutoGain(start, FftSize);
            _isMono = DetectMono(start, FftSize);

            for (var i = 0; i < FftSize; i++)
            {
                var w = _hamming[i];
                _fftBufL[i] = new Complex(_samples[start + i * 2] * gain * w, 0);
                _fftBufR[i] = new Complex(_samples[start + i * 2 + 1] * gain * w, 0);
            }
        }

        FftSharp.FFT.Forward(_fftBufL);
        FftSharp.FFT.Forward(_fftBufR);

        FillTargetFromMagnitudes(count, _fftBufL, _targetL);
        FillTargetFromMagnitudes(count, _fftBufR, _targetR);
        ApplyBassPump(count, _targetL);
        ApplyBassPump(count, _targetR);
        ApplyHighBoost(count, _targetL);
        ApplyHighBoost(count, _targetR);
        SmoothAndReturn(count, _targetL, _smoothedL, _peakHoldL);
        SmoothAndReturn(count, _targetR, _smoothedR, _peakHoldR);

        // 伪立体声：单声道源（L≈R）时给 R 侧加轻微时变扰动，避免左右完全一致
        if (_isMono)
        {
            var t = SimTime();
            for (var b = 0; b < count; b++)
                _smoothedR[b] *= (float)(1 + 0.06 * Math.Sin(b * 1.7 + t * 3));
        }

        UpdateBassEnergy(count);
        return new SpectrumFrame(_smoothedL, _smoothedR, _bassEnergy);
    }

    /// <summary>时域自动增益：帧峰值分段衰减估计，返回样本缩放倍率（替换 band 级归一化）。</summary>
    private float ComputeAutoGain(int start, int frames)
    {
        float peak = 0;
        for (var i = 0; i < frames * 2; i++)
        {
            var a = Math.Abs(_samples[start + i]);
            if (a > peak) peak = a;
        }
        if (peak > _maxDetectedVolume)
        {
            _maxDetectedVolume = peak;
        }
        else
        {
            var ratio = _maxDetectedVolume > 0 ? peak / _maxDetectedVolume : 0f;
            float decay = ratio < 0.2f ? 0.95f : ratio < 0.5f ? 0.99f : 0.9995f;
            _maxDetectedVolume *= decay;
        }
        _maxDetectedVolume = Math.Max(0.02f, _maxDetectedVolume);
        return (float)(1.0 / _maxDetectedVolume);
    }

    /// <summary>检测 L/R 是否近乎相同（单声道源）。</summary>
    private bool DetectMono(int start, int frames)
    {
        double diff = 0, sum = 0;
        for (var i = 0; i < frames; i++)
        {
            var l = _samples[start + i * 2];
            var r = _samples[start + i * 2 + 1];
            diff += Math.Abs(l - r);
            sum += Math.Abs(l) + Math.Abs(r);
        }
        return sum > 1e-6 && diff / sum < 0.05;
    }

    /// <summary>对数频段聚合 + 频段补偿曲线（低频平缓、中高频大幅抬升，对齐 BetterLyrics）。</summary>
    private void FillTargetFromMagnitudes(int count, Complex[] fftBuf, float[] target)
    {
        var nyquist = _sampleRate / 2.0;
        for (var b = 0; b < count; b++)
        {
            var f0 = MinFreq * Math.Pow((double)MaxFreq / MinFreq, (double)b / count);
            var f1 = MinFreq * Math.Pow((double)MaxFreq / MinFreq, (double)(b + 1) / count);
            var k0 = Math.Max(2, (int)(f0 / nyquist * (FftSize / 2)));
            var k1 = Math.Min(FftSize / 2, (int)(f1 / nyquist * (FftSize / 2)) + 1);
            // 保证每个频段至少 1 个 bin，避免低频段被置 0
            if (k1 <= k0)
                k1 = Math.Min(FftSize / 2, k0 + 1);
            double sum = 0;
            for (var k = k0; k < k1; k++)
                sum += fftBuf[k].Magnitude;
            var norm = sum / (k1 - k0) / (FftSize / 2.0);
            var gain = CompensationGain(f0, f1);
            target[b] = (float)Math.Clamp(norm * 2.0 * gain, 0, 1);
        }
    }

    /// <summary>频段补偿增益曲线（线性插值）：中高频大幅抬升，让高频区真正活跃。</summary>
    private static double CompensationGain(double fLo, double fHi)
    {
        ReadOnlySpan<double> freqs = [20, 50, 100, 200, 500, 1000, 2000, 4000, 8000, 12000, 16000, 20000];
        ReadOnlySpan<double> gains = [1.1, 1.1, 1.1, 1.2, 1.4, 1.5, 2.2, 3.2, 5.0, 6.5, 7.5, 8.0];
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

    /// <summary>低频律动调制（bass pump）：低频带平均能量放大全频谱，让整体随鼓点起伏。</summary>
    private void ApplyBassPump(int count, float[] target)
    {
        var bassN = Math.Min(7, count);
        double bass = 0;
        for (var b = 0; b < bassN; b++)
            bass += target[b];
        bass = bassN > 0 ? bass / bassN : 0;
        var pump = 1.0 + 0.2 * bass;
        for (var b = 0; b < count; b++)
            target[b] = (float)Math.Min(1.0, target[b] * pump);
    }

    /// <summary>高频活性增强：对后 1/3 频段做非线性指数提升，弱高频也能明显起伏。</summary>
    private void ApplyHighBoost(int count, float[] target)
    {
        var hiStart = count * 2 / 3;
        for (var b = hiStart; b < count; b++)
        {
            var t = (double)(b - hiStart) / Math.Max(1, count - 1 - hiStart);
            var exponent = 0.7 - 0.3 * t;
            target[b] = (float)Math.Min(1.0, Math.Pow(Math.Max(0.0001, target[b]), exponent));
        }
    }

    /// <summary>低音能量（低频前 7 band 平均，0~1），供渲染呼吸缩放。</summary>
    private void UpdateBassEnergy(int count)
    {
        var bassN = Math.Min(7, count);
        double bass = 0;
        for (var b = 0; b < bassN; b++)
            bass += _smoothedL[b];
        _bassEnergy = bassN > 0 ? (float)Math.Clamp(bass / bassN, 0, 1) : 0;
    }

    // ---------- 模拟频谱（伪立体声） ----------

    private SpectrumFrame ComputeSimulatedFrame()
    {
        var count = _smoothedL.Length;
        var t = SimTime();
        lock (_lock)
        {
            for (var b = 0; b < count; b++)
            {
                var x = b / (double)count;
                var vL =
                    0.5 + 0.5 * Math.Sin(x * 2.3 + t * 1.3)
                    + 0.5 + 0.5 * Math.Sin(x * 7.1 - t * 0.9)
                    + 0.5 + 0.5 * Math.Sin(x * 3.7 + t * 2.1 + 1.7);
                var vR =
                    0.5 + 0.5 * Math.Sin(x * 2.3 + t * 1.3 + 0.7)
                    + 0.5 + 0.5 * Math.Sin(x * 7.1 - t * 0.9 - 1.2)
                    + 0.5 + 0.5 * Math.Sin(x * 3.7 + t * 2.1 + 2.3);
                vL /= 3.0;
                vR /= 3.0;
                // 低频更饱满的高频衰减包络
                var envelope = 0.35 + 0.65 * Math.Pow(1 - x, 1.6);
                var level = _playingLevel;
                _targetL[b] = (float)(vL * envelope * level);
                _targetR[b] = (float)(vR * envelope * level);
            }
        }
        ApplyBassPump(count, _targetL);
        ApplyBassPump(count, _targetR);
        ApplyHighBoost(count, _targetL);
        ApplyHighBoost(count, _targetR);
        SmoothAndReturn(count, _targetL, _smoothedL, _peakHoldL);
        SmoothAndReturn(count, _targetR, _smoothedR, _peakHoldR);
        UpdateBassEnergy(count);
        return new SpectrumFrame(_smoothedL, _smoothedR, _bassEnergy);
    }

    /// <summary>统一模拟时钟（按真实流逝时间推进，与帧率解耦）。</summary>
    private double SimTime()
    {
        var now = _simClock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _lastSimT, 0, 0.1);
        _lastSimT = now;
        _simTime += dt;
        return _simTime;
    }

    // ---------- 平滑与输出 ----------

    private void SmoothAndReturn(int count, float[] target, float[] smoothed, float[] peakHold)
    {
        var attack = 1.0f / (1 + _smoothing);
        var decay = (float)Math.Pow(0.99, _smoothing);
        // 峰值保持：约 300ms 残影，弱频段不至于瞬间归零，频谱更饱满
        var hold = (float)Math.Pow(0.5, 1.0 / (0.3 * 30));
        for (var i = 0; i < count; i++)
        {
            peakHold[i] = Math.Max(target[i], peakHold[i] * hold);
            var t = (float)Math.Pow(Math.Clamp(peakHold[i], 0, 1), 0.7);
            var prev = smoothed[i];
            smoothed[i] = t > prev ? prev + (t - prev) * attack : prev * decay;
            if (smoothed[i] < 0.001f)
                smoothed[i] = 0;
        }
    }

    private void DecayToZero(int count)
    {
        var decay = (float)Math.Pow(0.96, _smoothing * 2);
        for (var i = 0; i < count; i++)
        {
            _smoothedL[i] *= decay;
            _smoothedR[i] *= decay;
        }
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

/// <summary>一帧频谱数据：L/R 频带快照 + 低音能量。数组为引擎内部复用，须当帧用完。</summary>
public readonly struct SpectrumFrame
{
    public float[] Left { get; }
    public float[] Right { get; }
    public float BassEnergy { get; }

    public SpectrumFrame(float[] left, float[] right, float bassEnergy)
    {
        Left = left;
        Right = right;
        BassEnergy = bassEnergy;
    }
}
