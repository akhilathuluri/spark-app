using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Spark.Models;
using Spark.Services;
using Spark.Services.Updates;

namespace Spark.Windows;

public partial class UpdatesWindow : Window
{
    private AppUpdateInfo? _currentInfo;
    private readonly UpdatePackageService _updatePackageService;
    private bool _isDownloadInProgress;

    public event Action? RefreshRequested;

    public UpdatesWindow()
    {
        InitializeComponent();
        _updatePackageService = new UpdatePackageService();
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
        SelectedRuntimeText.Text = "Selected runtime: Detecting...";
        VerificationResultText.Text = "Verification: Waiting for update metadata...";
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
            SelectedRuntimeText.Text = "Selected runtime: -";
            VerificationResultText.Text = "Verification: Not available.";
            ReleaseNotesText.Text = "No release details available.";
            CheckedAtText.Text = string.Empty;
            DownloadButton.IsEnabled = false;
            return;
        }

        CurrentVersionText.Text = $"Current version: {info.CurrentVersion}";
        LatestVersionText.Text = $"Latest version: {info.LatestVersion}";
        MinimumVersionText.Text = $"Minimum supported version: {info.MinimumVersion}";
        SelectedRuntimeText.Text = BuildSelectedRuntimeText(info);
        VerificationResultText.Text = BuildInitialVerificationText(info);
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

        DownloadButton.IsEnabled = CanDownloadVerifiedPackage(info);
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentInfo is null || _isDownloadInProgress)
            return;

        _isDownloadInProgress = true;
        DownloadButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;

        try
        {
            StatusText.Text = "Downloading and verifying update package...";
            VerificationResultText.Text = "Verification: Downloading package and validating SHA-256...";
            var result = await _updatePackageService.DownloadAndVerifyAsync(_currentInfo);
            if (!result.Succeeded)
            {
                StatusText.Text = "Update verification failed.";
                VerificationResultText.Text = $"Verification failed: {result.FailureReason}.";
                DebugLogService.LogMessage($"Update download verification failed: {result.FailureReason}. {result.Message}");

                var openInBrowser = MessageBox.Show(
                    $"{result.Message}{Environment.NewLine}{Environment.NewLine}Open download link in browser instead?",
                    "Spark Update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (openInBrowser == MessageBoxResult.Yes)
                    UpdateNotificationService.OpenDownloadUrl(_currentInfo.DownloadUrl);

                return;
            }

            StatusText.Text = "Package verified. Starting installer...";
            VerificationResultText.Text = "Verification passed: SHA-256 matched.";
            var continueInstall = MessageBox.Show(
                "Spark must close to complete the update. Continue?",
                "Spark Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (continueInstall != MessageBoxResult.Yes)
            {
                StatusText.Text = "Update ready. Close Spark and run installer to continue.";
                return;
            }

            var installerProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.FilePath)
            {
                UseShellExecute = true
            });

            if (installerProcess is null)
                throw new InvalidOperationException("Installer process could not be started.");

            StatusText.Text = "Installer launched. Closing Spark to release locked files...";
            _ = Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()), DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("UpdatesWindow.DownloadButton_Click", ex);
            StatusText.Text = "Unable to start the update installer.";
            VerificationResultText.Text = "Verification passed, but installer launch failed.";
            MessageBox.Show(
                "Could not launch installer after verification. You can retry from the Updates window.",
                "Spark Update",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isDownloadInProgress = false;
            RefreshButton.IsEnabled = true;
            DownloadButton.IsEnabled = _currentInfo is not null && CanDownloadVerifiedPackage(_currentInfo);
        }
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

    protected override void OnClosed(EventArgs e)
    {
        _updatePackageService.Dispose();
        base.OnClosed(e);
    }

    private static string BuildSelectedRuntimeText(AppUpdateInfo info)
    {
        var selected = string.IsNullOrWhiteSpace(info.SelectedRuntimeId)
            ? "unknown"
            : info.SelectedRuntimeId;
        var preferred = string.IsNullOrWhiteSpace(info.PreferredRuntimeId)
            ? "unknown"
            : info.PreferredRuntimeId;
        var fallbackText = info.UsedDownloadFallback ? "yes" : "no";

        return $"Selected runtime: {selected} (preferred: {preferred}, fallback: {fallbackText})";
    }

    private static string BuildInitialVerificationText(AppUpdateInfo info)
    {
        if (!info.IsUpdateAvailable)
            return "Verification: Not required (already up to date).";

        if (string.IsNullOrWhiteSpace(info.DownloadUrl))
            return "Verification: No download URL available.";

        if (string.IsNullOrWhiteSpace(info.DownloadSha256))
            return "Verification: SHA-256 missing from update metadata.";

        return "Verification: Ready (SHA-256 metadata available).";
    }

    private static bool CanDownloadVerifiedPackage(AppUpdateInfo info)
    {
        if (!info.IsUpdateAvailable)
            return false;

        if (string.IsNullOrWhiteSpace(info.DownloadUrl))
            return false;

        return !string.IsNullOrWhiteSpace(info.DownloadSha256);
    }
}
