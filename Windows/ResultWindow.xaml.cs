using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Text;
using Spark.Models;
using Spark.Services.FollowUps;
using Spark.Services.Prompts;
using Spark.Services;

namespace Spark.Windows;

public partial class ResultWindow : Window
{
    private readonly AiService _aiService;
    private readonly TextCaptureService _textCapture;
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly IconService _iconService = new();
    private readonly TextSanitizationService _textSanitizationService;
    private readonly UsageMetricsService _usageMetricsService;
    private readonly FollowUpActionService _followUpActionService = new();
    private readonly UserPromptPersonalizationService _promptPersonalizationService = new();
    private readonly InternetSearchService? _internetSearchService;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _diffCts;  // cancels in-flight diff computation on Regenerate
    private string _resultText = string.Empty;
    private readonly StringBuilder _rawResultBuilder = new();

    // Cursor position from the hotkey press — used to position the window near the user's work.
    private System.Windows.Point _lastCursorPos;

    // Remembered for Regenerate and FullEditor
    private ActionItem? _currentAction;
    private string _currentSelectedText = string.Empty;

    // Diff state
    private bool _isDiffAction;
    private bool _showingDiff;
    private bool _isFollowUpProcessing;

    // Streaming UI buffer: avoid dispatching one TextBox update per token.
    private readonly StringBuilder _pendingUiBuffer = new();
    private readonly object _uiBufferLock = new();
    private System.Windows.Threading.DispatcherTimer? _uiFlushTimer;

    // Returns the best available result text without O(n²) string allocation:
    //   During streaming  → _rawResultBuilder has all tokens so far (no "+=" needed)
    //   After stream ends → _resultText holds the final sanitised string
    private string CurrentResultText =>
        string.IsNullOrEmpty(_resultText) ? _rawResultBuilder.ToString() : _resultText;

    // Developer analytics
    private readonly AnalyticsService _analyticsService;
    private readonly DiagnosticsService? _diagnosticsService;

    // Action IDs where a before/after diff is meaningful
    private static readonly HashSet<string> DiffEligibleActions = new()
    {
        "fix_grammar", "rewrite", "improve",
        "change_tone", "casual_tone", "shorten", "expand",
        "length_50_words", "tweet_length", "one_paragraph", "bullet_summary"
    };

    public ResultWindow(
        AiService aiService,
        TextCaptureService textCapture,
        SettingsService settingsService,
        HistoryService historyService,
        UsageMetricsService usageMetricsService,
        AnalyticsService analyticsService,
        TextSanitizationService textSanitizationService,
        DiagnosticsService? diagnosticsService = null,
        InternetSearchService? internetSearchService = null)
    {
        _aiService = aiService;
        _textCapture = textCapture;
        _settingsService = settingsService;
        _historyService = historyService;
        _usageMetricsService = usageMetricsService;
        _analyticsService = analyticsService;
        _diagnosticsService = diagnosticsService;
        _textSanitizationService = textSanitizationService;
        _internetSearchService = internetSearchService;

        InitializeComponent();
    }

    // ── Entry point — safe to call multiple times on the same instance ───────

    public void ShowWithProcessing(ActionItem action, string selectedText,
        System.Windows.Point? cursorPos = null)
    {
        // Store cursor so PositionWindow() can place the result near the user's work.
        if (cursorPos.HasValue) _lastCursorPos = cursorPos.Value;

        // Cancel any in-flight AI stream and diff computation from a previous use.
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _diffCts?.Cancel();   // stop stale diff so it cannot overwrite the new result
        _diffCts?.Dispose();
        _diffCts = null;
        _resultText = string.Empty;
        _rawResultBuilder.Clear();
        ClearPendingUiBuffer();
        _isFollowUpProcessing = false;

        // Remember for Regenerate / Expand
        _currentAction       = action;
        _currentSelectedText = selectedText;

        // Reset all panels to the initial "thinking" state
        ThinkingPanel.Visibility  = Visibility.Visible;
        ResultPanel.Visibility    = Visibility.Collapsed;
        ErrorPanel.Visibility     = Visibility.Collapsed;
        DiffPanel.Visibility      = Visibility.Collapsed;
        FollowUpPanel.Visibility  = Visibility.Collapsed;
        ButtonPanel.Visibility    = Visibility.Collapsed;
        ExpandButton.Visibility   = Visibility.Collapsed;
        DiffToggleButton.Visibility = Visibility.Collapsed;
        FollowUpItems.ItemsSource = null;
        ResultText.Text           = string.Empty;
        ErrorText.Text            = string.Empty;

        // Reset diff state
        _showingDiff    = false;
        _isDiffAction   = DiffEligibleActions.Contains(action.Id) || action.Id.StartsWith("followup_", StringComparison.Ordinal);
        DiffToggleButton.Content  = "Changes";
        DiffToggleButton.ToolTip  = "Show changes from original";
        DiffText.Document = new FlowDocument();

        // Clear animation holds from previous close so XAML base values restore:
        //   RootBorder.Opacity → 0, ScaleT → 0.94, TranslateT.Y → -10
        RootBorder.BeginAnimation(OpacityProperty, null);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        TranslateT.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        ResultPanel.BeginAnimation(OpacityProperty, null);

        ActionIcon.Text  = action.Icon;
        ActionTitle.Text = action.Name;

        PositionWindow();

        Show();
        Activate();

        // Start animations explicitly every time (Loaded only fires on first Show)
        StartThinkingAnimation();
        AnimateOpen();

        _ = ProcessAsync(action, selectedText);
    }

    // ── Streaming processor ──────────────────────────────────────────────────

    private async Task ProcessAsync(ActionItem action, string selectedText)
    {
        var correlationId = _diagnosticsService?.NewCorrelationId() ?? Guid.NewGuid().ToString("N");
        _cts = new CancellationTokenSource();
        bool firstVisibleToken = true;
        bool hadLeadingNonVisibleTokens = false;
        const int SmoothRevealDelayMs = 60;
        const int LiveFlushIntervalMs = 35;
        EnsureUiFlushTimer();
        _uiFlushTimer!.Start();
        var liveFlushStopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Analytics: load provider info and start timer before any await
        var sw         = System.Diagnostics.Stopwatch.StartNew();
        int chunkCount = 0, resultLength = 0;
        var cfg        = _settingsService.Load();
        var provider   = cfg.ActiveProvider;
        var model      = provider switch
        {
            "Groq"   => cfg.Groq.Model,
            "OpenAI" => cfg.OpenAI?.Model ?? string.Empty,
            "Gemini" => cfg.Gemini?.Model ?? string.Empty,
            _        => cfg.OpenRouter.Model
        };
        _analyticsService.TrackActionStarted(action.Id, action.Name, action.IsBuiltIn, provider, model);

        try
        {
            await foreach (var token in _aiService.StreamAsync(action.Prompt!, selectedText, _cts.Token))
            {
                chunkCount++;
                resultLength += token.Length;
                _rawResultBuilder.Append(token);

                var t = _textSanitizationService.SanitizeChunk(token);
                if (string.IsNullOrEmpty(t))
                {
                    if (firstVisibleToken)
                        hadLeadingNonVisibleTokens = true;
                    continue;
                }

                bool flushFirstVisibleChunkNow = false;

                // Some providers stream leading newlines/spaces first. If we flip from
                // thinking to result on pure whitespace, users see an empty gap before text.
                if (firstVisibleToken)
                {
                    var visible = t.TrimStart();
                    if (string.IsNullOrEmpty(visible))
                    {
                        hadLeadingNonVisibleTokens = true;
                        continue;
                    }

                    t = visible;
                    firstVisibleToken = false;
                    flushFirstVisibleChunkNow = true;

                    // If the stream started with only non-visible chunks, hold a tiny
                    // beat before reveal so the transition feels less abrupt.
                    if (hadLeadingNonVisibleTokens)
                        await Task.Delay(SmoothRevealDelayMs, _cts.Token);
                }

                // First token flips from thinking state to result state once.
                if (flushFirstVisibleChunkNow)
                {
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        ThinkingPanel.Visibility = Visibility.Collapsed;
                        ResultPanel.Visibility   = Visibility.Visible;
                        ButtonPanel.Visibility   = Visibility.Visible;
                        ExpandButton.Visibility  = Visibility.Visible;
                        AnimateResultIn();
                    });
                }

                lock (_uiBufferLock)
                {
                    _pendingUiBuffer.Append(t);
                }

                if (flushFirstVisibleChunkNow)
                {
                    if (Dispatcher.CheckAccess())
                        FlushPendingUiBuffer();
                    else
                        _ = Dispatcher.BeginInvoke(FlushPendingUiBuffer);

                    liveFlushStopwatch.Restart();
                }
                else if (liveFlushStopwatch.ElapsedMilliseconds >= LiveFlushIntervalMs)
                {
                    // Fallback deterministic flush cadence. DispatcherTimer can be delayed
                    // under heavy UI load, which makes streamed tokens appear in one burst.
                    if (Dispatcher.CheckAccess())
                        FlushPendingUiBuffer();
                    else
                        _ = Dispatcher.BeginInvoke(FlushPendingUiBuffer);

                    liveFlushStopwatch.Restart();
                }
            }

            // Stream complete — ensure buttons are visible even if stream was empty
            Dispatcher.Invoke(() =>
            {
                FlushPendingUiBuffer();
                _uiFlushTimer?.Stop();

                ThinkingPanel.Visibility = Visibility.Collapsed;
                ButtonPanel.Visibility   = Visibility.Visible;

                // Final pass sanitization ensures consistent text before history/copy/replace.
                _resultText = _textSanitizationService.SanitizeFinal(_rawResultBuilder.ToString());
                var settings = _settingsService.Load();
                _resultText = _promptPersonalizationService.ApplyIdentityPlaceholders(_resultText, settings);
                ResultText.Text = _resultText;

                if (string.IsNullOrWhiteSpace(_resultText))
                {
                    ErrorPanel.Visibility = Visibility.Visible;
                    ErrorText.Text        = "No response received from the AI.";
                }
                else
                {
                    // Persist to history only when we have a complete result
                    _historyService.Add(new HistoryEntry
                    {
                        ActionId     = _currentAction!.Id,
                        ActionName   = _currentAction!.Name,
                        ActionIcon   = _currentAction!.Icon,
                        SelectedText = _currentSelectedText,
                        Result       = _resultText
                    });

                    // Build diff for text-transform actions (issue #10: large diff hang UI)
                    // Use AsyncDiffService to avoid blocking on very long text
                    if (_isDiffAction)
                    {
                        // Fresh CTS per result so Regenerate can cancel the stale diff.
                        _diffCts = new CancellationTokenSource();
                        _ = BuildDiffAsync(_currentSelectedText, _resultText, _diffCts.Token);
                        DiffToggleButton.Visibility = Visibility.Visible;
                    }

                    BindFollowUpActions();
                }
            });

            // Track completion — chunkCount is set synchronously in the foreach, always reliable
            if (chunkCount > 0)
                _analyticsService.TrackActionCompleted(
                    action.Id, action.IsBuiltIn, chunkCount, resultLength, sw.ElapsedMilliseconds, provider);

            if (!string.IsNullOrWhiteSpace(_resultText))
            {
                _usageMetricsService.RecordAction(
                    action.Id,
                    action.Name,
                    provider,
                    model,
                    selectedText,
                    _resultText,
                    (int)sw.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
            // Streaming cancelled (window reused or closed mid-stream).
            // Balance the orphaned TrackActionStarted so PostHog metrics stay coherent.
            if (chunkCount > 0)
                _analyticsService.TrackActionCancelled(
                    action.Id, action.IsBuiltIn, chunkCount, resultLength, sw.ElapsedMilliseconds, provider);
        }
        catch (Exception ex)
        {
            _uiFlushTimer?.Stop();
            _analyticsService.TrackActionError(action.Id, action.IsBuiltIn, ex.GetType().Name, provider);
            _diagnosticsService?.ReportHandledError("ResultWindow.ProcessAsync", ex, correlationId,
                new Dictionary<string, string>
                {
                    ["action_id"] = action.Id,
                    ["provider"] = provider
                });
            Dispatcher.Invoke(() =>
            {
                FlushPendingUiBuffer();
                ThinkingPanel.Visibility = Visibility.Collapsed;
                ErrorPanel.Visibility    = Visibility.Visible;
                ErrorText.Text           = ex.Message;
                ButtonPanel.Visibility   = Visibility.Visible;
            });
        }
        finally
        {
            _uiFlushTimer?.Stop();
        }
    }

    // ── Window positioning ──────────────────────────────────────────────────

    /// <summary>
    /// Positions the result window near the cursor captured at hotkey time.
    /// Falls back to screen-center when no cursor position is available.
    /// Applies screen-edge clamping so the window never leaves the working area.
    /// </summary>
    private void PositionWindow()
    {
        var screen = SystemParameters.WorkArea;

        // No cursor captured yet — fall back to screen-center (first-run / edge case).
        if (_lastCursorPos == default)
        {
            Left = (screen.Width  - Width)  / 2;
            Top  = (screen.Height - 400)    / 2;
            return;
        }

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        double winW = Width  > 0 ? Width  : 520;  // fallback if not yet rendered
        double winH = 400;                          // conservative height estimate

        double x = _lastCursorPos.X / dpi.DpiScaleX + 16;
        double y = _lastCursorPos.Y / dpi.DpiScaleY + 16;

        // Clamp so the window stays fully within the working area.
        if (x + winW > screen.Right)  x = screen.Right  - winW - 10;
        if (y + winH > screen.Bottom) y = screen.Bottom - winH - 10;
        if (x < screen.Left) x = screen.Left + 10;
        if (y < screen.Top)  y = screen.Top  + 10;

        Left = x;
        Top  = y;
    }

    private void AnimateResultIn()
    {
        var dur = new Duration(TimeSpan.FromMilliseconds(180));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var opAnim = new DoubleAnimation(0, 1, dur) { EasingFunction = ease };
        ResultPanel.BeginAnimation(OpacityProperty, opAnim);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var textToCopy = CurrentResultText;
        if (!string.IsNullOrEmpty(textToCopy))
            Clipboard.SetText(textToCopy);
        AnimateClose(showCopied: true);
    }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        var text = CurrentResultText;

        var validation = _textCapture.ValidateReplaceTarget();
        var settings = _settingsService.Load();
        bool forceReplace = false;
        bool isSparkForeground = validation.CurrentProcessName.Equals("Spark", StringComparison.OrdinalIgnoreCase);

        if (!validation.IsSafe && !isSparkForeground && ReplacePromptPreferenceService.ShouldShowUnsafePrompt())
        {
            var dialog = new UnsafeReplaceDialog(
                validation.SourceProcessName,
                validation.CurrentProcessName,
                allowCancel: true)
            {
                Owner = this
            };

            dialog.ShowDialog();
            if (dialog.DontAskAgainForSession)
                ReplacePromptPreferenceService.SuppressUnsafePromptForSession();

            if (dialog.Decision == UnsafeReplaceDecision.Cancel)
                return;

            if (dialog.Decision == UnsafeReplaceDecision.CopyOnly)
            {
                Clipboard.SetText(text);
                AnimateClose(showCopied: true);
                return;
            }

            forceReplace = true;
        }
        else if (!validation.IsSafe && !isSparkForeground && !ReplacePromptPreferenceService.ShouldShowUnsafePrompt())
        {
            forceReplace = true;
        }

        if (settings.SafeReplacePreviewMode)
        {
            var preview = text.Length > 220 ? text[..220] + "..." : text;
            var previewDecision = MessageBox.Show(
                $"Safe Preview:\n\n{preview}\n\nReplace selected text in {validation.CurrentProcessName}?",
                "Confirm Replace",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (previewDecision != MessageBoxResult.Yes)
                return;
        }

        AnimateClose(onComplete: async () =>
        {
            try
            {
                await Task.Delay(150); // allow window to fully close & source app to regain focus
                bool replaced = await _textCapture.ReplaceSelectedTextSafelyAsync(text, forceReplace);
                if (!replaced)
                {
                    var postValidation = _textCapture.ValidateReplaceTarget();
                    if (!ReplacePromptPreferenceService.ShouldShowUnsafePrompt())
                    {
                        await _textCapture.ReplaceSelectedTextSafelyAsync(text, force: true);
                        return;
                    }

                    var dialog = new UnsafeReplaceDialog(
                        postValidation.SourceProcessName,
                        postValidation.CurrentProcessName,
                        allowCancel: false)
                    {
                        // Owner=null: ResultWindow is hidden at this point; anchoring to a hidden
                        // window would place the dialog off-screen or at (0,0). null centres it
                        // on the primary screen safely.
                        Owner = null
                    };

                    dialog.ShowDialog();
                    if (dialog.DontAskAgainForSession)
                        ReplacePromptPreferenceService.SuppressUnsafePromptForSession();

                    if (dialog.Decision == UnsafeReplaceDecision.ReplaceAnyway)
                    {
                        await _textCapture.ReplaceSelectedTextSafelyAsync(text, force: true);
                        return;
                    }

                    Clipboard.SetText(text);
                }
            }
            catch { /* clipboard or focus errors — replacement already attempted, ignore */ }
        });
    }

    private void RegenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAction != null)
            ShowWithProcessing(_currentAction, _currentSelectedText);
    }

    private void DiffToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _showingDiff = !_showingDiff;

        if (_showingDiff)
        {
            ResultPanel.Visibility          = Visibility.Collapsed;
            DiffPanel.Visibility            = Visibility.Visible;
            DiffToggleButton.Content        = "Result";
            DiffToggleButton.ToolTip        = "Show AI result";
            AnimatePanelIn(DiffPanel);
        }
        else
        {
            DiffPanel.Visibility            = Visibility.Collapsed;
            ResultPanel.Visibility          = Visibility.Visible;
            DiffToggleButton.Content        = "Changes";
            DiffToggleButton.ToolTip        = "Show changes from original";
            AnimatePanelIn(ResultPanel);
        }
    }

    private void FollowUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isFollowUpProcessing)
            return;

        if (sender is not System.Windows.Controls.Button button)
            return;

        if (button.Tag is not FollowUpActionOption option)
            return;

        var sourceText = CurrentResultText;
        if (string.IsNullOrWhiteSpace(sourceText))
            return;

        _isFollowUpProcessing = true;
        var followUpAction = _followUpActionService.ToActionItem(option);
        followUpAction.Icon = _iconService.GetActionGlyph(option.IconActionId, _iconService.GetGlyph(IconKey.CustomAction));
        ShowWithProcessing(followUpAction, sourceText, _lastCursorPos);
    }

    private void BindFollowUpActions()
    {
        var options = _followUpActionService.GetSuggestedOptions(_currentAction, CurrentResultText);
        if (options.Count == 0)
        {
            FollowUpItems.ItemsSource = null;
            FollowUpPanel.Visibility = Visibility.Collapsed;
            return;
        }

        FollowUpItems.ItemsSource = options
            .Select(option => new FollowUpOptionViewModel
            {
                Option = option,
                Name = option.Name,
                IconGlyph = _iconService.GetActionGlyph(option.IconActionId, _iconService.GetGlyph(IconKey.CustomAction))
            })
            .ToList();
        FollowUpPanel.Visibility = Visibility.Visible;
    }

    private sealed class FollowUpOptionViewModel
    {
        public FollowUpActionOption Option { get; init; } = new();
        public string Name { get; init; } = string.Empty;
        public string IconGlyph { get; init; } = string.Empty;
    }

    private void AnimatePanelIn(UIElement panel)
    {
        var dur  = new Duration(TimeSpan.FromMilliseconds(160));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        panel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
    }

    // ── Diff rendering ───────────────────────────────────────────────────────

    // Frozen brushes: calling Freeze() releases WPF change-notification hooks and
    // allows GC to collect dependent objects that would otherwise be kept alive.
    private static T Frozen<T>(T freezable) where T : System.Windows.Freezable
        { freezable.Freeze(); return freezable; }

    private static readonly SolidColorBrush DeleteFg = Frozen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B)));   // warm red
    private static readonly SolidColorBrush InsertFg = Frozen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7E, 0xC8, 0x8B)));   // muted green
    private static readonly SolidColorBrush EqualFg  = Frozen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0xA8)));   // muted purple-gray
    private static readonly SolidColorBrush DeleteBg = Frozen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, 0xFF, 0x40, 0x40)));
    private static readonly SolidColorBrush InsertBg = Frozen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x28, 0x40, 0xFF, 0x60)));
    /// <summary>
    /// Async wrapper for diff computation using AsyncDiffService (issue #10: large diff hang UI).
    /// For small diffs, computes immediately. For large diffs, uses thread pool.
    /// Fire-and-forget: result is rendered when available, even if window was closed.
    /// </summary>
    private async Task BuildDiffAsync(string original, string result, CancellationToken ct = default)
    {
        try
        {
            var chunks = await AsyncDiffService.ComputeAsync(original, result, ct);

            // Skip render if window is hidden or Regenerate cancelled this diff.
            if (!IsVisible || ct.IsCancellationRequested) return;

            Dispatcher.Invoke(() => RenderDiffWithChunks(chunks));
        }
        catch (OperationCanceledException)
        {
            // Cancelled by Regenerate — expected, nothing to render.
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("ResultWindow.BuildDiffAsync", ex);
        }
    }

    private void RenderDiffWithChunks(List<DiffChunk> chunks)
    {
        var para = new Paragraph
        {
            Margin       = new Thickness(0),
            Padding      = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            FontFamily   = ResultText.FontFamily,
            FontSize     = ResultText.FontSize
        };

        foreach (var chunk in chunks)
        {
            var run = new Run(chunk.Text);
            switch (chunk.Type)
            {
                case DiffType.Delete:
                    run.Foreground     = DeleteFg;
                    run.Background     = DeleteBg;
                    run.TextDecorations = TextDecorations.Strikethrough;
                    break;
                case DiffType.Insert:
                    run.Foreground = InsertFg;
                    run.Background = InsertBg;
                    break;
                case DiffType.Equal:
                    run.Foreground = EqualFg;
                    break;
            }
            para.Inlines.Add(run);
        }

        var doc = new FlowDocument(para)
        {
            PagePadding = new Thickness(0),
            Background  = System.Windows.Media.Brushes.Transparent
        };

        DiffText.Document = doc;
    }

    private void RenderDiff(string original, string result)
    {
        var chunks = DiffService.Compute(original, result);

        var para = new Paragraph
        {
            Margin       = new Thickness(0),
            Padding      = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            FontFamily   = ResultText.FontFamily,
            FontSize     = ResultText.FontSize
        };

        foreach (var chunk in chunks)
        {
            var run = new Run(chunk.Text);
            switch (chunk.Type)
            {
                case DiffType.Delete:
                    run.Foreground     = DeleteFg;
                    run.Background     = DeleteBg;
                    run.TextDecorations = TextDecorations.Strikethrough;
                    break;
                case DiffType.Insert:
                    run.Foreground = InsertFg;
                    run.Background = InsertBg;
                    break;
                default: // Equal
                    run.Foreground = EqualFg;
                    break;
            }
            para.Inlines.Add(run);
        }

        var doc = new FlowDocument(para)
        {
            PagePadding = new Thickness(0),
            Background  = System.Windows.Media.Brushes.Transparent
        };

        DiffText.Document = doc;
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAction == null) return;
        var text = CurrentResultText;
        if (string.IsNullOrEmpty(text)) return;
        var editor = new FullEditorWindow(text, _currentAction, _textCapture);
        editor.Show();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => AnimateClose();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { AnimateClose(); e.Handled = true; }
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var textToCopy = CurrentResultText;
            if (!string.IsNullOrEmpty(textToCopy)) { Clipboard.SetText(textToCopy); AnimateClose(); e.Handled = true; }
        }
        base.OnKeyDown(e);
    }

    // ── Thinking bounce animation ────────────────────────────
    private void StartThinkingAnimation()
    {
        AnimateDot(DotT1, 0);
        AnimateDot(DotT2, 120);
        AnimateDot(DotT3, 240);
    }

    private static void AnimateDot(System.Windows.Media.TranslateTransform t, double delayMs)
    {
        var anim = new DoubleAnimation
        {
            From = 0, To = -5,
            Duration = new Duration(TimeSpan.FromMilliseconds(400)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        t.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, anim);
    }

    // ── Window animations ────────────────────────────────────
    private void AnimateOpen()
    {
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.94, TranslateT, -10, 220);
    }

    private void AnimateClose(bool showCopied = false, Action? onComplete = null)
    {
        _cts?.Cancel();
        _uiFlushTimer?.Stop();
        if (!IsVisible) { onComplete?.Invoke(); return; }

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
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnim);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void EnsureUiFlushTimer()
    {
        if (_uiFlushTimer != null) return;

        _uiFlushTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };

        // Use a named method rather than a lambda to avoid an unnecessary closure.
        _uiFlushTimer.Tick += OnUiFlushTimerTick;
    }

    private void OnUiFlushTimerTick(object? sender, EventArgs e) => FlushPendingUiBuffer();

    private void FlushPendingUiBuffer()
    {
        string append;
        lock (_uiBufferLock)
        {
            if (_pendingUiBuffer.Length == 0) return;
            append = _pendingUiBuffer.ToString();
            _pendingUiBuffer.Clear();
        }

        // Do NOT use "_resultText += append" here — that allocates a new string equal to the
        // full response length every 40 ms (O(n²) GC pressure on long responses).
        // CurrentResultText reads _rawResultBuilder directly during streaming instead.
        ResultText.AppendText(append);
        ResultText.ScrollToEnd();
    }

    private void ClearPendingUiBuffer()
    {
        lock (_uiBufferLock)
        {
            _pendingUiBuffer.Clear();
        }
    }
}
