using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Spark.Models;
using Spark.Services;

namespace Spark.Windows;

public class PriorityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var p = (value as string ?? string.Empty).Trim().ToLowerInvariant();
        return p switch
        {
            "high" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x90, 0x8A)),
            "low" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xD9, 0xA5)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xD8, 0x8E))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public partial class DeveloperNotificationsWindow : Window
{
    private IReadOnlyList<DeveloperNotification> _current = Array.Empty<DeveloperNotification>();

    public event Action? RefreshRequested;

    public DeveloperNotificationsWindow()
    {
        InitializeComponent();
    }

    public void ShowPanel()
    {
        RootBorder.BeginAnimation(OpacityProperty, null);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        TranslateT.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = (screen.Height - Height) / 2;

        Show();
        Activate();
        AnimateOpen();
    }

    public void SetCheckingState()
    {
        StatusText.Text = "Checking developer notifications...";
        CheckedAtText.Text = string.Empty;
    }

    public void SetNotifications(IReadOnlyList<DeveloperNotification> notifications)
    {
        _current = notifications;
        NotificationsList.ItemsSource = _current;

        if (_current.Count == 0)
            StatusText.Text = "No active developer notifications.";
        else
            StatusText.Text = $"{_current.Count} active notification(s).";

        CheckedAtText.Text = $"Last checked: {DateTime.Now:g}";
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        AnimateClose();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            AnimateClose();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void AnimateOpen()
    {
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.95, TranslateT, -10, 220);
    }

    private void AnimateClose()
    {
        if (!IsVisible)
            return;

        var duration = new Duration(TimeSpan.FromMilliseconds(160));
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn };

        var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        opacityAnimation.Completed += (_, _) => Hide();
        RootBorder.BeginAnimation(OpacityProperty, opacityAnimation);

        var scaleAnimation = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.96, duration) { EasingFunction = ease };
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnimation);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnimation);
    }
}
