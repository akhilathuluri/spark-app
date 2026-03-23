using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Spark.Models;
using Spark.Services;

namespace Spark.Windows;

public partial class AskWindow : Window
{
    private readonly AiService _aiService;
    private readonly TextCaptureService _textCapture;
    private readonly SettingsService _settingsService;
    private readonly ResultWindow _resultWindow;
    private string _selectedText = string.Empty;

    public AskWindow(AiService aiService, TextCaptureService textCapture, SettingsService settingsService, ResultWindow resultWindow)
    {
        _aiService = aiService;
        _textCapture = textCapture;
        _settingsService = settingsService;
        _resultWindow = resultWindow;

        InitializeComponent();
        Deactivated += (_, _) => AnimateClose();
    }

    public void ShowWithText(string selectedText, System.Windows.Point cursorPos)
    {
        _selectedText = selectedText;

        // Show truncated preview of the selected text
        ContextPreview.Text = selectedText.Length > 130
            ? selectedText[..130].TrimEnd() + "…"
            : selectedText;
        ContextBorder.Visibility = string.IsNullOrWhiteSpace(selectedText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        PromptBox.Text = string.Empty;
        Placeholder.Visibility = Visibility.Visible;
        AskButton.IsEnabled = false;

        PositionNearCursor(cursorPos);

        WindowGpuAnimationService.ResetOpenState(RootBorder, ScaleT, 0.92, TranslateT, 8);
        Show();
        Activate();
        AnimateOpen();
        PromptBox.Focus();
    }

    private void PositionNearCursor(System.Windows.Point cursor)
    {
        var screen = SystemParameters.WorkArea;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        double x = cursor.X / dpi.DpiScaleX + 12;
        double y = cursor.Y / dpi.DpiScaleY + 12;

        if (x + 440 > screen.Right)  x = screen.Right - 440 - 10;
        if (y + 320 > screen.Bottom) y = y - 320 - 20;
        if (x < screen.Left)  x = screen.Left + 10;
        if (y < screen.Top)   y = screen.Top + 10;

        Left = x;
        Top  = y;
    }

    // ── Input handlers ───────────────────────────────────────────────────────

    private void PromptBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var hasText = !string.IsNullOrWhiteSpace(PromptBox.Text);
        Placeholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        AskButton.IsEnabled    = hasText;
    }

    private void PromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
            e.Handled = true;
        }
    }

    private void AskButton_Click(object sender, RoutedEventArgs e) => Submit();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => AnimateClose();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            AnimateClose();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    // ── Submit ───────────────────────────────────────────────────────────────

    private void Submit()
    {
        var userInput = PromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userInput)) return;

        // Build prompt: embed the user's question; {text} is the AiService placeholder
        // for the selected text. If there is no selection, just answer the question directly.
        var prompt = string.IsNullOrWhiteSpace(_selectedText)
            ? userInput
            : $"{userInput}\n\nHere is the text for reference:\n\n{{text}}";

        var action = new ActionItem
        {
            Id       = "ask_ai_result",
            Name     = "Ask AI",
            Icon     = "💬",
            IsBuiltIn = true,
            Prompt   = prompt
        };

        var selectedText = _selectedText;

        AnimateClose(onComplete: () =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _resultWindow.ShowWithProcessing(action, selectedText);
            }));
        });
    }

    // ── Animations ───────────────────────────────────────────────────────────

    private void AnimateOpen()
    {
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.92, TranslateT, 8);
    }

    private bool _closing = false;

    private void AnimateClose(Action? onComplete = null)
    {
        if (!IsVisible || _closing) { onComplete?.Invoke(); return; }
        _closing = true;

        var dur  = new Duration(TimeSpan.FromMilliseconds(160));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var opAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnim.Completed += (_, _) =>
        {
            _closing = false;
            Hide();
            onComplete?.Invoke();
        };
        RootBorder.BeginAnimation(OpacityProperty, opAnim);

        var scaleAnim = new DoubleAnimation(1.0, 0.94, dur) { EasingFunction = ease };
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnim);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnim);
    }
}
