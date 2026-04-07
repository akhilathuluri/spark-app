using System.Windows;
using System.Windows.Input;
using Spark.Services;

namespace Spark.Windows;

public partial class OnboardingWizardWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly TextCaptureService _textCaptureService;
    private readonly AiService _aiService;
    private readonly Action _onCompleted;
    private bool _completionRaised;

    private bool _apiValid;
    private bool _hotkeyValid;
    private bool _captureValid;
    private bool _aiValid;

    public OnboardingWizardWindow(
        SettingsService settingsService,
        TextCaptureService textCaptureService,
        AiService aiService,
        Action onCompleted)
    {
        _settingsService = settingsService;
        _textCaptureService = textCaptureService;
        _aiService = aiService;
        _onCompleted = onCompleted;

        InitializeComponent();
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settingsService)
        {
            Owner = this
        };

        win.ShowDialog();
        ValidateApi();
        ValidateHotkey();
    }

    private void ValidateApiButton_Click(object sender, RoutedEventArgs e) => ValidateApi();

    private void ValidateApi()
    {
        var settings = _settingsService.Load();

        var provider = settings.ActiveProvider;
        bool keyOk = provider == "Groq"
            ? !string.IsNullOrWhiteSpace(settings.Groq.ApiKey)
            : !string.IsNullOrWhiteSpace(settings.OpenRouter.ApiKey);

        bool modelOk = provider == "Groq"
            ? !string.IsNullOrWhiteSpace(settings.Groq.Model)
            : !string.IsNullOrWhiteSpace(settings.OpenRouter.Model);

        _apiValid = keyOk && modelOk;
        ApiStatusText.Text = _apiValid
            ? $"Status: [OK] {provider} key + model configured"
            : $"Status: Missing {provider} key/model";

        UpdateFinishState();
    }

    private void ValidateHotkeyButton_Click(object sender, RoutedEventArgs e) => ValidateHotkey();

    private void ValidateHotkey()
    {
        var settings = _settingsService.Load();

        _hotkeyValid = !string.IsNullOrWhiteSpace(settings.HotkeyModifiers)
                       && !string.IsNullOrWhiteSpace(settings.HotkeyKey);

        HotkeyStatusText.Text = _hotkeyValid
            ? $"Status: [OK] {settings.HotkeyModifiers}+{settings.HotkeyKey}"
            : "Status: Missing hotkey";

        UpdateFinishState();
    }

    private async void CaptureTestButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureTestButton.IsEnabled = false;
        ProgressText.Text = "Select text in any app now. Starting in 3s...";

        await Task.Delay(700);
        Hide();

        try
        {
            await Task.Delay(3000);
            var text = await _textCaptureService.GetSelectedTextAsync();

            _captureValid = !string.IsNullOrWhiteSpace(text);
            CaptureStatusText.Text = _captureValid
                ? $"Status: [OK] Captured {_textCaptureService.LastDiagnostics.ProcessName} ({text!.Length} chars)"
                : "Status: Capture failed. Select text and retry.";
        }
        catch (Exception ex)
        {
            _captureValid = false;
            CaptureStatusText.Text = "Status: Capture failed due to runtime error.";
            DebugLogService.LogError("OnboardingWizard.CaptureTest", ex);
        }
        finally
        {
            Show();
            Activate();
            CaptureTestButton.IsEnabled = true;
            ProgressText.Text = string.Empty;
            UpdateFinishState();
        }
    }

    private async void QuickTestButton_Click(object sender, RoutedEventArgs e)
    {
        QuickTestButton.IsEnabled = false;
        ProgressText.Text = "Running quick AI test...";
        ResultPreviewBox.Text = string.Empty;

        try
        {
            const string sample = "please improve this sentence and make it clearer for users";
            const string prompt = "Rewrite this sentence for clarity while preserving meaning. Return only the rewritten sentence:\n\n{text}";

            var response = await _aiService.ProcessAsync(prompt, sample);
            _aiValid = !string.IsNullOrWhiteSpace(response);

            AiStatusText.Text = _aiValid
                ? "Status: [OK] Quick AI test succeeded"
                : "Status: Quick AI test returned empty output";

            ResultPreviewBox.Text = string.IsNullOrWhiteSpace(response)
                ? "No output returned."
                : response.Trim();
        }
        catch (Exception ex)
        {
            _aiValid = false;
            AiStatusText.Text = "Status: Quick AI test failed (check API key/model).";
            ResultPreviewBox.Text = ex.Message;
            DebugLogService.LogError("OnboardingWizard.QuickTest", ex);
        }
        finally
        {
            ProgressText.Text = string.Empty;
            QuickTestButton.IsEnabled = true;
            UpdateFinishState();
        }
    }

    private void UpdateFinishState()
    {
        FinishButton.IsEnabled = _apiValid && _hotkeyValid && _captureValid && _aiValid;
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseCompletionOnce();
        Close();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseCompletionOnce();
        Close();
    }

    private void RaiseCompletionOnce()
    {
        if (_completionRaised)
            return;

        _completionRaised = true;
        _onCompleted.Invoke();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.96);
        ValidateApi();
        ValidateHotkey();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SkipButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        RaiseCompletionOnce();
        base.OnClosing(e);
    }
}
