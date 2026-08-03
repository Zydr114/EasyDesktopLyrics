using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Interop;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;
using EasyDesktopLyrics.Services.Providers;
using EasyDesktopLyrics.ViewModels;
using EasyDesktopLyrics.Views;

namespace EasyDesktopLyrics;

public sealed class App : Application
{
    private SettingsService _settingsService = null!;
    private SmtcService _smtcService = null!;
    private LyricsOrchestrator _orchestrator = null!;
    private LyricsCache _lyricsCache = null!;
    private OverridesStore _overridesStore = null!;
    private LyricsOverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;

    public App()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // ---- 组装服务 ----
        _settingsService = new SettingsService();
        var settings = _settingsService.Current;

        AutoStart.Sync(settings.AutoStart);

        _lyricsCache = new LyricsCache();
        _overridesStore = new OverridesStore();
        ILyricsProvider[] providers = CreateProviders(_settingsService);

        _smtcService = new SmtcService();
        _orchestrator = new LyricsOrchestrator(_smtcService, _settingsService, _lyricsCache, _overridesStore, providers);

        // ---- 托盘 ----
        var overlayVm = new OverlayViewModel(_settingsService, _orchestrator, _smtcService);
        _overlay = new LyricsOverlayWindow(overlayVm, _settingsService);

        _overlay.AnchorChanged += (x, y) =>
            _settingsService.Update(s => { s.AnchorX = x; s.AnchorY = y; });

        var trayVisible = new NativeMenuItem("显示歌词")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = settings.LyricsVisible,
        };
        trayVisible.Click += (_, _) =>
            _settingsService.Update(s => s.LyricsVisible = !s.LyricsVisible);

        var trayLock = new NativeMenuItem("锁定歌词（鼠标穿透）")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = settings.Locked,
        };
        trayLock.Click += (_, _) =>
            _settingsService.Update(s => s.Locked = !s.Locked);

        var trayMenu = new NativeMenu();
        trayMenu.Add(trayVisible);
        trayMenu.Add(trayLock);
        trayMenu.Add(new NativeMenuItemSeparator());

        var traySettings = new NativeMenuItem("设置…");
        traySettings.Click += (_, _) => OpenSettings();
        trayMenu.Add(traySettings);
        trayMenu.Add(new NativeMenuItemSeparator());

        var trayExit = new NativeMenuItem("退出");
        trayExit.Click += (_, _) => Cleanup();
        trayMenu.Add(trayExit);

        _settingsService.Changed += () =>
        {
            AutoStart.Sync(_settingsService.Current.AutoStart);
            _smtcService.SetPreferredSession(_settingsService.Current.LockedSessionAumid);
            _smtcService.SetPlayerRules(_settingsService.Current.PlayerPriorities);
            _overlay?.UpdateVisibility();
            _overlay?.SetLocked(_settingsService.Current.Locked);
            trayVisible.IsChecked = _settingsService.Current.LyricsVisible;
            trayLock.IsChecked = _settingsService.Current.Locked;
        };

        var icon = LoadWindowIcon("Assets/app.png");
        var trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "EasyDesktopLyrics",
            Menu = trayMenu,
        };
        trayIcon.Clicked += (_, _) => OpenSettings();

        TrayIcon.SetIcons(this, [trayIcon]);

        _overlay.Show();

        // ---- 启动 SMTC ----
        _ = _smtcService.StartAsync();
        _smtcService.SetPlayerRules(_settingsService.Current.PlayerPriorities);

        // ---- 退出处理 ----
        desktop.ShutdownRequested += (_, _) => Cleanup();

        base.OnFrameworkInitializationCompleted();
    }

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        var vm = new SettingsViewModel(
            _settingsService, _smtcService, _orchestrator,
            _overridesStore, _lyricsCache,
            CreateProviders(_settingsService));

        vm.SnapToPreset = preset => _overlay?.SnapToPreset(preset);
        vm.SetAnchor = (x, y) => _overlay?.SetAnchor(x, y);
        vm.ScreenArea = _overlay?.Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        _settingsWindow = new SettingsWindow(vm);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private static ILyricsProvider[] CreateProviders(SettingsService settings) =>
    [
        new NeteaseLyricsProvider(),
        new QQMusicLyricsProvider(),
        new LrclibLyricsProvider(),
        new LocalLrcProvider(settings),
    ];

    private void Cleanup()
    {
        try
        {
            if (_overlay != null)
                _overlay.AllowClose();
            _overlay?.PersistAnchor();
            _overlay?.Close();
            _smtcService.Dispose();
            _settingsService.Flush();
        }
        catch (Exception ex)
        {
            Log.Error("cleanup", ex);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static WindowIcon LoadWindowIcon(string path)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://EasyDesktopLyrics/{path}"));
        return new WindowIcon(stream);
    }
}
