using System.Windows;
using System.Windows.Input;
using Spark.Services;

namespace Spark.Windows;

public partial class OnboardingWizardWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly AiService _aiService;
    private readonly Action _onCompleted;
    private bool _completionRaised;

    private bool _apiValid;
    private bool _hotkeyValid;
    private bool _aiValid;

    public OnboardingWizardWindow(
        SettingsService settingsService,
        TextCaptureService textCaptureService,
        AiService aiService,
        Action onCompleted)
    {
        _settingsService = settingsService;
        _aiService = aiService;
        _onCompleted = onCompleted;

        InitializeComponent();

        var iconService = new IconService();
        OnboardingIconText.Text = iconService.GetGlyph(IconKey.Improve);
        if (string.IsNullOrWhiteSpace(OnboardingIconText.Text))
            OnboardingIconText.Text = iconService.GetGlyph(IconKey.CustomAction);
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

    private void ValidateApiButton_Click(object sender, RoutedEventArgs e)
    {
        ValidateApi();
        ValidateHotkey();
    }

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
            ? $"AI: Ready ({provider})"
            : $"AI: Add {provider} API key and model";

        UpdateFinishState();
    }

    private void ValidateHotkey()
    {
        var settings = _settingsService.Load();

        _hotkeyValid = !string.IsNullOrWhiteSpace(settings.HotkeyModifiers)
                       && !string.IsNullOrWhiteSpace(settings.HotkeyKey);

        HotkeyStatusText.Text = _hotkeyValid
            ? $"Shortcut: Ready ({settings.HotkeyModifiers}+{settings.HotkeyKey})"
            : "Shortcut: Choose a hotkey in Settings";

        UpdateFinishState();
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
                ? "AI test: Success"
                : "AI test: No output returned";

            ResultPreviewBox.Text = string.IsNullOrWhiteSpace(response)
                ? "No output returned."
                : response.Trim();
        }
        catch (Exception ex)
        {
            _aiValid = false;
            AiStatusText.Text = "AI test: Failed. Check key/model in Settings.";
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
        var completed = 0;
        if (_apiValid && _hotkeyValid) completed++;
        if (_aiValid) completed++;

        SetupProgressText.Text = $"Progress: {completed} of 2 complete";
        FooterText.Text = completed == 2
            ? "All steps complete. You can finish now."
            : "Finish unlocks after both checks pass.";

        FinishButton.IsEnabled = _apiValid && _hotkeyValid && _aiValid;
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
