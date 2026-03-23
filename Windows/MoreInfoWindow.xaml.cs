using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Spark.Models;
using Spark.Services;

namespace Spark.Windows;

public partial class MoreInfoWindow : Window
{
    private readonly DeveloperInfo _developerInfo;

    public MoreInfoWindow(DeveloperInfo developerInfo)
    {
        _developerInfo = developerInfo;
        InitializeComponent();
        PopulateInfo();
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

    private void PopulateInfo()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = $"{version?.Major}.{version?.Minor}.{version?.Build ?? 0}";

        AppDetailsText.Text =
            $"App: {_developerInfo.AppName}\n" +
            $"Version: {versionText}\n" +
            "Platform: .NET 10 WPF desktop app";

        DeveloperNameText.Text = $"Developer: {_developerInfo.DeveloperName}";
        DeveloperMessageText.Text = _developerInfo.DeveloperMessage;
    }

    private void ContactDeveloper_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_developerInfo.ContactUrl);
    }

    private void ReportIssue_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(_developerInfo.ReportIssueUrl);
    }

    private static void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore failures to open browser links.
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        AnimateClose();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
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
        {
            return;
        }

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
