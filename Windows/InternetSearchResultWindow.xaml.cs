using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Spark.Models;
using Spark.Services;

namespace Spark.Windows;

public partial class InternetSearchResultWindow : Window
{
    private readonly InternetSearchService _internetSearchService;
    private readonly HistoryService _historyService;
    private readonly UsageMetricsService _usageMetricsService;
    private readonly AnalyticsService _analyticsService;
    private readonly DiagnosticsService? _diagnosticsService;
    private readonly IconService _iconService = new();
    private CancellationTokenSource? _cts;
    private System.Windows.Point _lastCursorPos;
    private ActionItem? _currentAction;
    private string _currentQuery = string.Empty;

    public InternetSearchResultWindow(
        InternetSearchService internetSearchService,
        HistoryService historyService,
        UsageMetricsService usageMetricsService,
        AnalyticsService analyticsService,
        DiagnosticsService? diagnosticsService = null)
    {
        _internetSearchService = internetSearchService;
        _historyService = historyService;
        _usageMetricsService = usageMetricsService;
        _analyticsService = analyticsService;
        _diagnosticsService = diagnosticsService;

        InitializeComponent();
    }

    public void ShowWithSearch(ActionItem action, string query, System.Windows.Point? cursorPos = null)
    {
        if (cursorPos.HasValue) _lastCursorPos = cursorPos.Value;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _currentAction = action;
        _currentQuery = query;

        // Reset UI
        ThinkingPanel.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        ButtonPanel.Visibility = Visibility.Collapsed;
        AnswerCard.Visibility = Visibility.Collapsed;
        ResultsHeader.Visibility = Visibility.Collapsed;
        ResultsList.ItemsSource = null;
        AnswerText.Text = string.Empty;
        ErrorText.Text = string.Empty;
        ResultCountLabel.Text = string.Empty;

        ActionIcon.Text = action.Icon;
        ActionTitle.Text = action.Name;
        QueryText.Text = $"Query: {query}";

        // Set icons using IconService
        AiAnswerIcon.Text = _iconService.GetGlyph(IconKey.AskAi);
        SearchResultsIcon.Text = _iconService.GetGlyph(IconKey.Search);

        // Clear animations
        RootBorder.BeginAnimation(OpacityProperty, null);
        ScaleT.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ScaleT.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        TranslateT.BeginAnimation(TranslateTransform.YProperty, null);

        PositionWindow();

        Show();
        Activate();

        StartThinkingAnimation();
        AnimateOpen();

        _ = ProcessSearchAsync(action, query);
    }

    private async Task ProcessSearchAsync(ActionItem action, string query)
    {
        var correlationId = _diagnosticsService?.NewCorrelationId() ?? Guid.NewGuid().ToString("N");
        _cts = new CancellationTokenSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _analyticsService.TrackActionStarted(action.Id, action.Name, action.IsBuiltIn, "Tavily", "search");

        try
        {
            var searchResult = await _internetSearchService.SearchAsync(query, _cts.Token);

            Dispatcher.Invoke(() =>
            {
                ThinkingPanel.Visibility = Visibility.Collapsed;
                ResultsPanel.Visibility = Visibility.Visible;
                ButtonPanel.Visibility = Visibility.Visible;

                // Show AI Answer if available
                if (!string.IsNullOrWhiteSpace(searchResult.Answer))
                {
                    AnswerCard.Visibility = Visibility.Visible;
                    AnswerText.Text = searchResult.Answer;
                }

                // Show Search Results
                if (searchResult.Results.Count > 0)
                {
                    ResultsHeader.Visibility = Visibility.Visible;

                    var resultItems = searchResult.Results.Take(5).Select(r => new SearchResultViewModel
                    {
                        Title = r.Title,
                        Url = r.Url,
                        ContentSnippet = TruncateContent(r.Content, 150)
                    }).ToList();

                    ResultsList.ItemsSource = resultItems;
                    ResultCountLabel.Text = $"{resultItems.Count} result{(resultItems.Count != 1 ? "s" : "")} found";
                }
                else
                {
                    ResultCountLabel.Text = "No results found";
                }

                AnimateResultIn();

                // Build result text for history
                var resultText = BuildResultText(searchResult);

                // Persist to history
                _historyService.Add(new HistoryEntry
                {
                    ActionId = action.Id,
                    ActionName = action.Name,
                    ActionIcon = action.Icon,
                    SelectedText = query,
                    Result = resultText
                });

                _usageMetricsService.RecordAction(
                    action.Id,
                    action.Name,
                    "Tavily",
                    "search",
                    query,
                    resultText,
                    (int)sw.ElapsedMilliseconds);
            });

            _analyticsService.TrackActionCompleted(
                action.Id, action.IsBuiltIn, 1, searchResult.Answer?.Length ?? 0, sw.ElapsedMilliseconds, "Tavily");
        }
        catch (OperationCanceledException)
        {
            _analyticsService.TrackActionCancelled(action.Id, action.IsBuiltIn, 0, 0, sw.ElapsedMilliseconds, "Tavily");
        }
        catch (Exception ex)
        {
            _analyticsService.TrackActionError(action.Id, action.IsBuiltIn, ex.GetType().Name, "Tavily");
            _diagnosticsService?.ReportHandledError("InternetSearchResultWindow.ProcessSearchAsync", ex, correlationId,
                new Dictionary<string, string>
                {
                    ["action_id"] = action.Id,
                    ["provider"] = "Tavily"
                });

            Dispatcher.Invoke(() =>
            {
                ThinkingPanel.Visibility = Visibility.Collapsed;
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorText.Text = ex.Message;
                ButtonPanel.Visibility = Visibility.Visible;
            });
        }
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;

        return content.Substring(0, maxLength) + "...";
    }

    private static string BuildResultText(TavilySearchResult searchResult)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(searchResult.Answer))
        {
            sb.AppendLine("AI Answer:");
            sb.AppendLine(searchResult.Answer);
            sb.AppendLine();
        }

        if (searchResult.Results.Count > 0)
        {
            sb.AppendLine("Search Results:");
            for (int i = 0; i < Math.Min(searchResult.Results.Count, 5); i++)
            {
                var result = searchResult.Results[i];
                sb.AppendLine($"{i + 1}. {result.Title}");
                sb.AppendLine($"   {result.Url}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private void ResultCard_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x26, 0x26, 0x38));
        }
    }

    private void ResultCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x2E));
        }
    }

    private void ResultCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                DebugLogService.LogError("InternetSearchResultWindow.OpenUrl", ex);
            }
        }
    }

    private void PositionWindow()
    {
        var screen = SystemParameters.WorkArea;

        if (_lastCursorPos == default)
        {
            Left = (screen.Width - Width) / 2;
            Top = (screen.Height - Height) / 2;
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        double winW = Width > 0 ? Width : 580;
        double winH = Height > 0 ? Height : 620;

        double x = _lastCursorPos.X / dpi.DpiScaleX + 16;
        double y = _lastCursorPos.Y / dpi.DpiScaleY + 16;

        if (x + winW > screen.Right) x = screen.Right - winW - 10;
        if (y + winH > screen.Bottom) y = screen.Bottom - winH - 10;
        if (x < screen.Left) x = screen.Left + 10;
        if (y < screen.Top) y = screen.Top + 10;

        Left = x;
        Top = y;
    }

    private void AnimateResultIn()
    {
        var dur = new Duration(TimeSpan.FromMilliseconds(180));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var opAnim = new DoubleAnimation(0, 1, dur) { EasingFunction = ease };
        ResultsPanel.BeginAnimation(OpacityProperty, opAnim);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => AnimateClose();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            AnimateClose();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void StartThinkingAnimation()
    {
        AnimateDot(DotT1, 0);
        AnimateDot(DotT2, 120);
        AnimateDot(DotT3, 240);
    }

    private static void AnimateDot(TranslateTransform t, double delayMs)
    {
        var anim = new DoubleAnimation
        {
            From = 0,
            To = -5,
            Duration = new Duration(TimeSpan.FromMilliseconds(400)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        t.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    private void AnimateOpen()
    {
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.94, TranslateT, -10, 220);
    }

    private void AnimateClose(Action? onComplete = null)
    {
        _cts?.Cancel();
        if (!IsVisible)
        {
            onComplete?.Invoke();
            return;
        }

        var dur = new Duration(TimeSpan.FromMilliseconds(160));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var opAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnim.Completed += (_, _) =>
        {
            Hide();
            onComplete?.Invoke();
        };
        RootBorder.BeginAnimation(OpacityProperty, opAnim);

        var scaleAnim = new DoubleAnimation(1.0, 0.95, dur) { EasingFunction = ease };
        ScaleT.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        ScaleT.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private class SearchResultViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string ContentSnippet { get; set; } = string.Empty;
    }
}
