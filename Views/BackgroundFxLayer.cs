using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 背景动效层：承载 频谱 / 飘雪 / 雾层 三个相互独立的子效果，
/// 共用单个渲染定时器（帧率由设置控制）。默认全部关闭 = 零开销。
/// </summary>
public sealed class BackgroundFxLayer : Grid
{
    private const int MinFps = 30;
    private const int MaxFps = 120;

    private readonly SettingsService _settings;
    private readonly SpectrumFxControl _spectrum = new();
    private readonly SnowFxControl _snow = new();
    private readonly FogFxControl _fog = new();
    private readonly DispatcherTimer _timer;
    private long _lastTick;
    private bool _windowVisible;
    private bool _playing;
    private bool _spectrumRunning;

    public BackgroundFxLayer(SettingsService settings)
    {
        _settings = settings;
        IsHitTestVisible = false;
        Children.Add(_spectrum);
        Children.Add(_snow);
        Children.Add(_fog);

        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => OnTick();
        _settings.Changed += OnSettingsChanged;
        OnSettingsChanged();
    }

    /// <summary>窗口可见性（窗口隐藏时动效停止，与暂停无关）。</summary>
    public void SetWindowVisible(bool visible)
    {
        if (_windowVisible == visible)
            return;
        _windowVisible = visible;
        ApplyVisibility();
    }

    /// <summary>歌词行区域（layer 本地坐标），用于频谱“行中央”定位；空 rect 回退整层。</summary>
    public void SetLyricsRect(Rect rect) =>
        _spectrum.LyricsRect = rect.Width > 0 && rect.Height > 0 ? rect : null;

    /// <summary>当前曲目封面（雾层取主色）。</summary>
    public void SetCoverImage(IImage? image) => _fog.SetCoverImage(image);

    /// <summary>窗口物理屏幕矩形提供者（雾层背后取色）。</summary>
    public void SetScreenRect(Func<(int X, int Y, int W, int H)> provider) => _fog.SetScreenRectProvider(provider);

    /// <summary>播放状态（频谱引擎驱动）。</summary>
    public void SetPlaying(bool playing) => _playing = playing;

    private void OnSettingsChanged()
    {
        var bf = _settings.Current.BackgroundFx;
        var fps = Math.Clamp(bf.Fps, MinFps, MaxFps);
        _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
        _spectrum.ApplySettings(bf.Spectrum);
        _snow.ApplySettings(bf.Snow);
        _fog.ApplySettings(bf.Fog);
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        var s = _settings.Current.BackgroundFx;
        var anyEnabled = s.Spectrum.Enabled || s.Snow.Enabled || s.Fog.Enabled;

        _spectrum.IsVisible = _windowVisible && s.Spectrum.Enabled;
        _snow.IsVisible = _windowVisible && s.Snow.Enabled;
        _fog.IsVisible = _windowVisible && s.Fog.Enabled;

        if (s.Spectrum.Enabled && !_spectrumRunning)
        {
            _spectrumRunning = true;
            _spectrum.Start();
        }
        else if (!s.Spectrum.Enabled && _spectrumRunning)
        {
            _spectrumRunning = false;
            _spectrum.Stop();
        }

        if (anyEnabled && _windowVisible && !_timer.IsEnabled)
        {
            _lastTick = Environment.TickCount64;
            _timer.Start();
        }
        else if ((!anyEnabled || !_windowVisible) && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    private void OnTick()
    {
        var now = Environment.TickCount64;
        var dt = Math.Max(0.001, (now - _lastTick) / 1000.0);
        _lastTick = now;

        if (_spectrum.IsVisible)
        {
            _spectrum.SetPlaying(_playing);
            _spectrum.TickAndInvalidate();
        }
        if (_snow.IsVisible)
            _snow.TickAndInvalidate(dt);
        if (_fog.IsVisible)
            _fog.TickAndInvalidate(dt);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
        _settings.Changed -= OnSettingsChanged;
        _spectrum.Stop();
        _fog.Dispose();
    }
}
