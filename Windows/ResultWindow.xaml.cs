using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Text;
using Scriptly.Models;
using Scriptly.Services;

namespace Scriptly.Windows;

public partial class ResultWindow : Window
{
    private readonly AiService _aiService;
    private readonly TextCaptureService _textCapture;
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly TextSanitizationService _textSanitizationService;
    private CancellationTokenSource? _cts;
    private string _resultText = string.Empty;
    private readonly StringBuilder _rawResultBuilder = new();

    // Remembered for Regenerate and FullEditor
    private ActionItem? _currentAction;
    private string _currentSelectedText = string.Empty;

    // Diff state
    private bool _isDiffAction;
    private bool _showingDiff;

    // Streaming UI buffer: avoid dispatching one TextBox update per token.
    private readonly StringBuilder _pendingUiBuffer = new();
    private readonly object _uiBufferLock = new();
    private System.Windows.Threading.DispatcherTimer? _uiFlushTimer;

    // Developer analytics
    private readonly AnalyticsService _analyticsService;
    private readonly DiagnosticsService? _diagnosticsService;

    // Action IDs where a before/after diff is meaningful
    private static readonly HashSet<string> DiffEligibleActions = new()
    {
        "fix_grammar", "rewrite", "improve",
        "change_tone", "casual_tone", "shorten", "expand"
    };

    public ResultWindow(
        AiService aiService,
        TextCaptureService textCapture,
        SettingsService settingsService,
        HistoryService historyService,
        AnalyticsService analyticsService,
        DiagnosticsService? diagnosticsService = null)
    {
        _aiService = aiService;
        _textCapture = textCapture;
        _settingsService = settingsService;
        _historyService = historyService;
        _analyticsService = analyticsService;
        _diagnosticsService = diagnosticsService;
        _textSanitizationService = new TextSanitizationService();

        InitializeComponent();
    }

    // ── Entry point — safe to call multiple times on the same instance ───────

    public void ShowWithProcessing(ActionItem action, string selectedText)
    {
        // Cancel and dispose any in-flight request from a previous use
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _resultText = string.Empty;
        _rawResultBuilder.Clear();
        ClearPendingUiBuffer();

        // Remember for Regenerate / Expand
        _currentAction       = action;
        _currentSelectedText = selectedText;

        // Reset all panels to the initial "thinking" state
        ThinkingPanel.Visibility  = Visibility.Visible;
        ResultPanel.Visibility    = Visibility.Collapsed;
        ErrorPanel.Visibility     = Visibility.Collapsed;
        DiffPanel.Visibility      = Visibility.Collapsed;
        ButtonPanel.Visibility    = Visibility.Collapsed;
        ExpandButton.Visibility   = Visibility.Collapsed;
        DiffToggleButton.Visibility = Visibility.Collapsed;
        ResultText.Text           = string.Empty;
        ErrorText.Text            = string.Empty;

        // Reset diff state
        _showingDiff    = false;
        _isDiffAction   = DiffEligibleActions.Contains(action.Id);
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

        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top  = (screen.Height - 400) / 2;

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
        bool firstToken = true;
        EnsureUiFlushTimer();
        _uiFlushTimer!.Start();

        // Analytics: load provider info and start timer before any await
        var sw         = System.Diagnostics.Stopwatch.StartNew();
        int chunkCount = 0, resultLength = 0;
        var cfg        = _settingsService.Load();
        var provider   = cfg.ActiveProvider;
        var model      = provider == "Groq" ? cfg.Groq.Model : cfg.OpenRouter.Model;
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
                    continue;

                // First token flips from thinking state to result state once.
                if (firstToken)
                {
                    firstToken = false;
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
                        _ = BuildDiffAsync(_currentSelectedText, _resultText);  // fire-and-forget
                        DiffToggleButton.Visibility = Visibility.Visible;
                    }
                }
            });

            // Track completion — chunkCount is set synchronously in the foreach, always reliable
            if (chunkCount > 0)
                _analyticsService.TrackActionCompleted(
                    action.Id, action.IsBuiltIn, chunkCount, resultLength, sw.ElapsedMilliseconds, provider);
        }
        catch (OperationCanceledException)
        {
            // Window was closed or reused before stream completed — expected, ignore
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

    private void AnimateResultIn()
    {
        var dur = new Duration(TimeSpan.FromMilliseconds(180));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var opAnim = new DoubleAnimation(0, 1, dur) { EasingFunction = ease };
        ResultPanel.BeginAnimation(OpacityProperty, opAnim);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_resultText))
            Clipboard.SetText(_resultText);
        AnimateClose(showCopied: true);
    }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        var text = _resultText;

        var validation = _textCapture.ValidateReplaceTarget();
        var settings = _settingsService.Load();
        bool forceReplace = false;
        bool isScriptlyForeground = validation.CurrentProcessName.Equals("Scriptly", StringComparison.OrdinalIgnoreCase);

        if (!validation.IsSafe && !isScriptlyForeground && ReplacePromptPreferenceService.ShouldShowUnsafePrompt())
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
        else if (!validation.IsSafe && !isScriptlyForeground && !ReplacePromptPreferenceService.ShouldShowUnsafePrompt())
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
                        Owner = this
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

    private void AnimatePanelIn(UIElement panel)
    {
        var dur  = new Duration(TimeSpan.FromMilliseconds(160));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        panel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
    }

    // ── Diff rendering ───────────────────────────────────────────────────────

    private static readonly SolidColorBrush DeleteFg =
        new(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));   // warm red
    private static readonly SolidColorBrush InsertFg =
        new(System.Windows.Media.Color.FromRgb(0x7E, 0xC8, 0x8B));   // muted green
    private static readonly SolidColorBrush EqualFg  =
        new(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0xA8));   // muted purple-gray
    private static readonly SolidColorBrush DeleteBg =
        new(System.Windows.Media.Color.FromArgb(0x28, 0xFF, 0x40, 0x40));
    private static readonly SolidColorBrush InsertBg =
        new(System.Windows.Media.Color.FromArgb(0x28, 0x40, 0xFF, 0x60));
    /// <summary>
    /// Async wrapper for diff computation using AsyncDiffService (issue #10: large diff hang UI).
    /// For small diffs, computes immediately. For large diffs, uses thread pool.
    /// Fire-and-forget: result is rendered when available, even if window was closed.
    /// </summary>
    private async Task BuildDiffAsync(string original, string result)
    {
        try
        {
            // Use AsyncDiffService to avoid blocking UI on very long text
            var chunks = await AsyncDiffService.ComputeAsync(original, result);
            
            // Check if window is still visible before rendering (user may have closed it)
            if (!IsVisible) return;

            // Render on UI thread
            Dispatcher.Invoke(() => RenderDiffWithChunks(chunks));
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
        if (_currentAction == null || string.IsNullOrEmpty(_resultText)) return;
        var editor = new FullEditorWindow(_resultText, _currentAction, _textCapture);
        editor.Show();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => AnimateClose();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { AnimateClose(); e.Handled = true; }
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && !string.IsNullOrEmpty(_resultText))
        { Clipboard.SetText(_resultText); AnimateClose(); e.Handled = true; }
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

        _uiFlushTimer.Tick += (_, _) => FlushPendingUiBuffer();
    }

    private void FlushPendingUiBuffer()
    {
        string append;
        lock (_uiBufferLock)
        {
            if (_pendingUiBuffer.Length == 0) return;
            append = _pendingUiBuffer.ToString();
            _pendingUiBuffer.Clear();
        }

        _resultText += append;
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
