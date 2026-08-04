using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EasyDesktopLyrics.ViewModels;

namespace EasyDesktopLyrics.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly Control[] _views;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        // 分区视图实例缓存：切换导航时零创建开销
        _views =
        [
            new PlaySettingsView(),
            new AppearanceSettingsView(),
            new TextEffectsSettingsView(),
            new CoverSettingsView(),
            new DisplaySettingsView(),
            new FixSettingsView(),
            new BackgroundFxSettingsView(),
        ];
        ContentHost.Content = _views[Math.Clamp(vm.SelectedNav, 0, 6)];

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.SelectedNav))
            {
                ContentHost.Content = _views[Math.Clamp(_vm.SelectedNav, 0, 6)];
                ContentScroll.Offset = Vector.Zero;
            }
        };

        // 数值文本框：回车确认输入并移到下一焦点（触发 LostFocus 提交 TwoWay 绑定）
        AddHandler(InputElement.KeyDownEvent, OnEnterKeyDown, RoutingStrategies.Tunnel);

        Opened += (_, _) => FitToScreen();
    }

    private void OnEnterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Source is not TextBox)
            return;
        // 移到下一可聚焦元素；失败则把焦点还给窗口，两者都会触发当前 TextBox 的 LostFocus 提交
        if (!(FocusManager?.TryMoveFocus(NavigationDirection.Next) ?? false))
            Focus();
        e.Handled = true;
    }

    /// <summary>
    /// 依据主屏工作区（物理像素 × 缩放）限制窗口尺寸并重新居中。
    /// CenterScreen 只在初始尺寸下计算一次，尺寸调整后需手动定位。
    /// </summary>
    private void FitToScreen()
    {
        var scr = Screens.Primary;
        if (scr == null)
            return;
        var maxW = scr.WorkingArea.Width / scr.Scaling;
        var maxH = scr.WorkingArea.Height / scr.Scaling;
        Width = Math.Min(780, Math.Max(660, maxW * 0.8));
        Height = Math.Min(700, maxH * 0.85);
        MinWidth = Math.Min(660, Width);
        MinHeight = Math.Min(540, Height);

        // 以工作区中心重新定位（Position 为物理像素）
        var wa = scr.WorkingArea;
        Position = new PixelPoint(
            (int)(wa.X + (wa.Width - Width * scr.Scaling) / 2),
            (int)(wa.Y + (wa.Height - Height * scr.Scaling) / 2));
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }
}
