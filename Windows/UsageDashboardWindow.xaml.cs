using System.Windows;
using System.Windows.Input;
using Spark.Services;

namespace Spark.Windows;

public partial class UsageDashboardWindow : Window
{
    private readonly UsageMetricsService _usageMetricsService;
    private readonly IconService _iconService = new();

    public UsageDashboardWindow(UsageMetricsService usageMetricsService)
    {
        _usageMetricsService = usageMetricsService;
        InitializeComponent();

        var glyph = _iconService.GetGlyph(IconKey.UsageDashboard);
        if (string.IsNullOrWhiteSpace(glyph))
            glyph = _iconService.GetGlyph(IconKey.MoreInfo);

        TitleIconText.Text = glyph;
    }

    public void ShowPanel()
    {
        Refresh();
        Show();
        Activate();
        AnimateOpen();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var summary = _usageMetricsService.GetCurrentMonthSummary();
        var recent = _usageMetricsService.GetRecentRecords(20);

        TotalRunsText.Text = summary.TotalRuns.ToString("N0");
        AvgLatencyText.Text = summary.TotalRuns == 0 ? "0 ms" : $"{summary.AvgLatencyMs:N0} ms";
        TotalTokensText.Text = summary.TotalEstimatedTokens.ToString("N0");
        MonthlyCostText.Text = $"${summary.EstimatedMonthlyCostUsd:F4}";

        ActionsGrid.ItemsSource = summary.Actions;

        RecentList.ItemsSource = recent.Select(r =>
            $"{r.TimestampUtc.ToLocalTime():MMM dd HH:mm}  {r.ActionName}  {r.LatencyMs}ms  {r.EstimatedInputTokens + r.EstimatedOutputTokens} tokens  ${r.EstimatedCostUsd:F4}").ToList();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => AnimateClose();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            AnimateClose();

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

        var dur = new Duration(TimeSpan.FromMilliseconds(140));
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn };

        var opacity = new System.Windows.Media.Animation.DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opacity.Completed += (_, _) => Hide();
        RootBorder.BeginAnimation(OpacityProperty, opacity);
    }
}
