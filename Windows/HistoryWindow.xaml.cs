using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Spark.Models;
using Spark.Services;

namespace Spark.Windows;

public partial class HistoryWindow : Window
{
    private readonly HistoryService _historyService;
    private readonly LocalizationService _loc;

    public HistoryWindow(HistoryService historyService)
    {
        _historyService = historyService;
        var settings = new SettingsService().Load();
        _loc = new LocalizationService(settings.Language);
        InitializeComponent();
        ApplyLocalization();

        // Live-update the list if the window is already visible when a result finishes
        _historyService.Changed += () =>
        {
            if (IsVisible)
                Dispatcher.Invoke(RefreshList);
        };
    }

    /// <summary>Refresh entries and animate the window open. Safe to call repeatedly.</summary>
    public void ShowPanel()
    {
        RefreshList();

        // Reset any animation hold-overs from the previous close
        RootBorder.BeginAnimation(OpacityProperty, null);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        TranslateT.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

        var screen = SystemParameters.WorkArea;
        Left = (screen.Width  - Width)  / 2;
        Top  = (screen.Height - Height) / 2;

        Show();
        Activate();
        AnimateOpen();
    }

    private void RefreshList()
    {
        EntriesScroll.ScrollToTop();

        var entries   = _historyService.Entries;
        bool hasItems = entries.Count > 0;

        EntriesList.ItemsSource  = null;
        EntriesList.ItemsSource  = entries;

        EmptyPanel.Visibility    = hasItems ? Visibility.Collapsed : Visibility.Visible;
        EntriesScroll.Visibility = hasItems ? Visibility.Visible   : Visibility.Collapsed;
        ClearAllButton.Visibility = hasItems ? Visibility.Visible  : Visibility.Collapsed;
        CountLabel.Text          = hasItems ? $"({entries.Count})" : string.Empty;
    }

    private void CopyEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HistoryEntry entry && !string.IsNullOrEmpty(entry.Result))
        {
            Clipboard.SetText(entry.Result);

            FooterHint.Text = _loc.T("history.copied", "✓ Copied to clipboard");
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (_, _) => { FooterHint.Text = _loc.T("history.footerHint", "Click Copy on any entry to copy the result"); timer.Stop(); };
            timer.Start();
        }
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _historyService.Clear();
        RefreshList();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => AnimateClose();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { AnimateClose(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    // ── Animations ───────────────────────────────────────────────────────────

    private void AnimateOpen()
    {
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.94, TranslateT, -10, 220);
    }

    private void AnimateClose()
    {
        if (!IsVisible) return;

        var dur  = new Duration(TimeSpan.FromMilliseconds(160));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var opAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnim.Completed += (_, _) => Hide();
        RootBorder.BeginAnimation(OpacityProperty, opAnim);

        var scaleAnim = new DoubleAnimation(1.0, 0.95, dur) { EasingFunction = ease };
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnim);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void ApplyLocalization()
    {
        HistoryTitleText.Text = _loc.T("history.title", "History");
        ClearAllButton.Content = _loc.T("history.clearAll", "Clear all");
        NoHistoryText.Text = _loc.T("history.emptyTitle", "No history yet");
        NoHistoryHintText.Text = _loc.T("history.emptyHint", "Processed results will appear here");
        FooterHint.Text = _loc.T("history.footerHint", "Click Copy on any entry to copy the result");
    }
}
