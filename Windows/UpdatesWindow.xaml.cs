using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Spark.Models;
using Spark.Services;

namespace Spark.Windows;

public partial class UpdatesWindow : Window
{
    private AppUpdateInfo? _currentInfo;

    public event Action? RefreshRequested;

    public UpdatesWindow()
    {
        InitializeComponent();
        DownloadButton.IsEnabled = false;
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
        StatusText.Text = "Checking for updates...";
        CurrentVersionText.Text = "Current version: -";
        LatestVersionText.Text = "Latest version: -";
        MinimumVersionText.Text = "Minimum supported version: -";
        ReleaseNotesText.Text = "Fetching release notes...";
        CheckedAtText.Text = string.Empty;
        DownloadButton.IsEnabled = false;
    }

    public void SetUpdateInfo(AppUpdateInfo? info)
    {
        _currentInfo = info;

        if (info is null)
        {
            StatusText.Text = "Unable to check updates right now.";
            CurrentVersionText.Text = "Current version: -";
            LatestVersionText.Text = "Latest version: -";
            MinimumVersionText.Text = "Minimum supported version: -";
            ReleaseNotesText.Text = "No release details available.";
            CheckedAtText.Text = string.Empty;
            DownloadButton.IsEnabled = false;
            return;
        }

        CurrentVersionText.Text = $"Current version: {info.CurrentVersion}";
        LatestVersionText.Text = $"Latest version: {info.LatestVersion}";
        MinimumVersionText.Text = $"Minimum supported version: {info.MinimumVersion}";
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes)
            ? "No release notes provided."
            : info.ReleaseNotes;

        CheckedAtText.Text = $"Last checked: {info.CheckedAtUtc.ToLocalTime():g}";

        if (info.IsUpdateAvailable)
        {
            StatusText.Text = info.IsRequiredUpdate
                ? "A required update is available."
                : "A new update is available.";
        }
        else
        {
            StatusText.Text = "You are on the latest version.";
        }

        DownloadButton.IsEnabled = info.IsUpdateAvailable && !string.IsNullOrWhiteSpace(info.DownloadUrl);
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentInfo is null)
            return;

        UpdateNotificationService.OpenDownloadUrl(_currentInfo.DownloadUrl);
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
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var opacityAnimation = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        opacityAnimation.Completed += (_, _) => Hide();
        RootBorder.BeginAnimation(OpacityProperty, opacityAnimation);

        var scaleAnimation = new DoubleAnimation(1.0, 0.96, duration) { EasingFunction = ease };
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnimation);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnimation);
    }
}
