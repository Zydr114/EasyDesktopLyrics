using System.Numerics;
using EasyDesktopLyrics.Infrastructure;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// 频谱引擎：优先 WASAPI 环回采集真实音频做 FFT；采集不可用时自动回退到
/// 播放状态驱动的模拟频谱。对外暴露 32 个归一化频带（0~1），供渲染层按需读取。
/// </summary>
public sealed class SpectrumEngine : IDisposable
{
    private const int FftSize = 1024;
    private const int BandCount = 32;
    private const int MinFreq = 40;
    private const int MaxFreq = 16000;

    private readonly object _lock = new();
    private readonly List<float> _samples = new(FftSize * 2);
    private readonly Complex[] _fftBuf = new Complex[FftSize];
    private readonly float[] _target = new float[BandCount];
    private readonly float[] _smoothed = new float[BandCount];
    private readonly WasapiLoopbackCapture? _capture;
    private bool _real;
    private bool _disposed;
    private int _sampleRate = 48000;

    // 模拟频谱状态
    private double _simTime;
    private double _playingLevel;

    public SpectrumEngine()
    {
        _capture = new WasapiLoopbackCapture();
        _capture.DataAvailable += OnData;
    }

    /// <summary>true = 真实环回频谱；false = 模拟回退。</summary>
    public bool IsReal => _real;

    public int Count => BandCount;

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

    /// <summary>当前频带快照（长度 BandCount，0~1，内部复用数组）。</summary>
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
        lock (_lock)
        {
            if (_samples.Count < FftSize)
            {
                DecayToZero();
                return _smoothed;
            }

            var start = _samples.Count - FftSize;
            for (var i = 0; i < FftSize; i++)
                _fftBuf[i] = new Complex(_samples[start + i], 0);
        }

        Fft(_fftBuf);
        FillTargetFromMagnitudes();
        return SmoothAndReturn();
    }

    private void FillTargetFromMagnitudes()
    {
        var nyquist = _sampleRate / 2.0;
        for (var b = 0; b < BandCount; b++)
        {
            var f0 = MinFreq * Math.Pow((double)MaxFreq / MinFreq, (double)b / BandCount);
            var f1 = MinFreq * Math.Pow((double)MaxFreq / MinFreq, (double)(b + 1) / BandCount);
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
            _target[b] = (float)Math.Clamp(norm * 2.0, 0, 1);
        }
    }

    // ---------- 模拟频谱 ----------

    private float[] ComputeSimulatedBands()
    {
        lock (_lock)
        {
            _simTime += 0.033;
            for (var b = 0; b < BandCount; b++)
            {
                var x = b / (double)BandCount;
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
        return SmoothAndReturn();
    }

    // ---------- 平滑与输出 ----------

    private float[] SmoothAndReturn()
    {
        var attack = 1.0f / (1 + _smoothing);
        var decay = (float)Math.Pow(0.985, _smoothing * 2);
        for (var i = 0; i < BandCount; i++)
        {
            var t = _target[i];
            var prev = _smoothed[i];
            _smoothed[i] = t > prev ? prev + (t - prev) * attack : prev * decay;
            if (_smoothed[i] < 0.001f)
                _smoothed[i] = 0;
        }
        return _smoothed;
    }

    private void DecayToZero()
    {
        var decay = (float)Math.Pow(0.96, _smoothing * 2);
        for (var i = 0; i < BandCount; i++)
            _smoothed[i] *= decay;
    }

    // ---------- FFT ----------

    private static void Fft(Span<Complex> data)
    {
        var n = data.Length;
        for (var i = 1; i < n; i++)
        {
            var j = 0;
            var bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }
            j ^= bit;
            if (i < j)
                (data[i], data[j]) = (data[j], data[i]);
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
            for (var i = 0; i < n; i += len)
            {
                var w = new Complex(1, 0);
                for (var k = 0; k < len / 2; k++)
                {
                    var u = data[i + k];
                    var v = data[i + k + len / 2] * w;
                    data[i + k] = u + v;
                    data[i + k + len / 2] = u - v;
                    w *= wlen;
                }
            }
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
