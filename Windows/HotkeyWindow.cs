using System.Windows;

namespace Spark.Windows;

/// <summary>
/// An invisible window used solely as a message pump target
/// for RegisterHotKey. Never shown, never in taskbar.
/// </summary>
public class HotkeyWindow : Window
{
    public HotkeyWindow()
    {
        Width = 0;
        Height = 0;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Opacity = 0;
        Left = -9999;
        Top = -9999;
        IsHitTestVisible = false;
    }
}
