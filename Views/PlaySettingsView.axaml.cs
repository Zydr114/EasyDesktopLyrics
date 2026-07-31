using Avalonia.Controls;
using EasyDesktopLyrics.ViewModels;

namespace EasyDesktopLyrics.Views;

public sealed partial class PlaySettingsView : UserControl
{
    private const double RowHeight = 36;

    public PlaySettingsView()
    {
        InitializeComponent();

        var playerReorder = new ReorderDragController(PlayerList, RowHeight, PlayerGhostPopup, PlayerGhostText);
        playerReorder.MoveRequested += (from, to) =>
            (DataContext as SettingsViewModel)?.MovePlayerRule(from, to);

        var sourceReorder = new ReorderDragController(SourceList, RowHeight, SourceGhostPopup, SourceGhostText);
        sourceReorder.MoveRequested += (from, to) =>
            (DataContext as SettingsViewModel)?.MoveLyricSource(from, to);
    }
}
