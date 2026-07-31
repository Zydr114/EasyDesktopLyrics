using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 可拖拽重排列表的通用交互控制器：
/// 固定行高坐标定位 + 指针捕获 + 浮动副本（ghost）跟手 + 拖动项淡化占位。
/// 宿主为 ItemsControl（非虚拟化），由 XAML 提供行模板与 ghost Popup。
/// </summary>
internal sealed class ReorderDragController
{
    private readonly ItemsControl _host;
    private readonly double _rowHeight;
    private readonly Popup _popup;
    private readonly TextBlock _ghostText;
    private int _dragIndex = -1;
    private Control? _dragContainer;

    /// <summary>请求把 from 位置的条目移动到 to 位置（宿主负责更新数据源）。</summary>
    public event Action<int, int>? MoveRequested;

    public ReorderDragController(ItemsControl host, double rowHeight, Popup popup, TextBlock ghostText)
    {
        _host = host;
        _rowHeight = rowHeight;
        _popup = popup;
        _ghostText = ghostText;
        _host.PointerPressed += OnPointerPressed;
        _host.PointerMoved += OnPointerMoved;
        _host.PointerReleased += OnPointerReleased;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
            return;
        var index = IndexAt(e.GetPosition(_host).Y);
        if (index < 0)
            return;

        _dragIndex = index;
        _dragContainer = _host.ContainerFromIndex(index);
        if (_dragContainer != null)
            _dragContainer.Opacity = 0.3;

        _ghostText.Text = FindGhostText(_dragContainer);
        UpdateGhost(e.GetPosition(_host));
        _popup.IsOpen = true;
        _host.Cursor = new Cursor(StandardCursorType.Hand);

        e.Pointer.Capture(_host);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragIndex < 0)
            return;
        if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
        {
            Reset();
            return;
        }
        UpdateGhost(e.GetPosition(_host));

        var target = IndexAt(e.GetPosition(_host).Y);
        if (target >= 0 && target != _dragIndex)
        {
            MoveRequested?.Invoke(_dragIndex, target);
            _dragIndex = target;

            // 淡化标记跟随新的拖拽容器（Move 后容器重排）
            if (_dragContainer != null)
                _dragContainer.Opacity = 1.0;
            _dragContainer = _host.ContainerFromIndex(_dragIndex);
            if (_dragContainer != null)
                _dragContainer.Opacity = 0.3;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        Reset();
    }

    private int IndexAt(double y)
    {
        var index = (int)(y / _rowHeight);
        return index >= 0 && index < _host.ItemCount ? index : -1;
    }

    private void UpdateGhost(Point p)
    {
        _popup.HorizontalOffset = p.X - 8;
        _popup.VerticalOffset = p.Y - 32;
    }

    private void Reset()
    {
        _dragIndex = -1;
        if (_dragContainer != null)
        {
            _dragContainer.Opacity = 1.0;
            _dragContainer = null;
        }
        _host.Cursor = null;
        _popup.IsOpen = false;
    }

    private static string FindGhostText(Control? container)
    {
        if (container == null)
            return "";
        return container.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault()?.Text ?? "";
    }
}
