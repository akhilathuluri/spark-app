using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using Spark.Models;
using Spark.Services;

namespace Spark.Windows;

public partial class FullEditorWindow : Window
{
    private readonly TextCaptureService _textCapture;

    public FullEditorWindow(string resultText, ActionItem action, TextCaptureService textCapture)
    {
        _textCapture = textCapture;
        InitializeComponent();

        TitleIcon.Text  = action.Icon;
        TitleText.Text  = action.Name;
        ResultTextBox.Text = resultText;
        UpdateWordCount();
    }

    // ── Resize support for AllowsTransparency=True windows ───────────────────

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        src?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x84) return IntPtr.Zero; // WM_NCHITTEST only

        // Decode screen-space cursor position from LPARAM (sign-extend correctly)
        int x = unchecked((short)(uint)lParam);
        int y = unchecked((short)((uint)lParam >> 16));

        // Convert to WPF device-independent pixels
        var src = HwndSource.FromHwnd(hwnd);
        if (src?.CompositionTarget == null) return IntPtr.Zero;
        var pt = src.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(x, y));

        const double g = 8; // grip zone width in DIPs
        bool l = pt.X < Left + g,       r = pt.X > Left + Width  - g;
        bool t = pt.Y < Top  + g,       b = pt.Y > Top  + Height - g;

        if (b && r) { handled = true; return (IntPtr)17; } // HTBOTTOMRIGHT
        if (b && l) { handled = true; return (IntPtr)16; } // HTBOTTOMLEFT
        if (t && r) { handled = true; return (IntPtr)14; } // HTTOPRIGHT
        if (t && l) { handled = true; return (IntPtr)13; } // HTTOPLEFT
        if (l)      { handled = true; return (IntPtr)10; } // HTLEFT
        if (r)      { handled = true; return (IntPtr)11; } // HTRIGHT
        if (t)      { handled = true; return (IntPtr)12; } // HTTOP
        if (b)      { handled = true; return (IntPtr)15; } // HTBOTTOM

        return IntPtr.Zero;
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void ResultTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateWordCount();

    private void UpdateWordCount()
    {
        var text  = ResultTextBox.Text;
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' },
                               StringSplitOptions.RemoveEmptyEntries).Length;
        WordCountLabel.Text = $"{words:N0} words · {text.Length:N0} chars";
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var text = ResultTextBox.Text;
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        var text = ResultTextBox.Text;
        var validation = _textCapture.ValidateReplaceTarget();
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
                return;
            }

            forceReplace = true;
        }
        else if (!validation.IsSafe && !isSparkForeground && !ReplacePromptPreferenceService.ShouldShowUnsafePrompt())
        {
            forceReplace = true;
        }

        Close();
        // Dispatcher.BeginInvoke runs on the STA UI thread — required for Clipboard.SetText.
        // Task.Run uses MTA thread-pool threads which cause ThreadStateException on clipboard calls.
        _ = Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(150); // let window fully close before injecting keystrokes
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
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)               { Close(); e.Handled = true; }
        if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
                                               { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    // ── Open animation ───────────────────────────────────────────────────────

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.97);
    }
}
