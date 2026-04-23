using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using Spark.Models;
using Spark.Services;
using Spark.Services.Actions;

namespace Spark.Windows;

public partial class SettingsWindow : Window
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private readonly SettingsService _settingsService;
    private readonly BuiltInActionCatalogService _builtInCatalog = new();
    private readonly BuiltInActionPreferencesService _builtInPreferences = new();
    private readonly IconService _iconService = new();
    private readonly LocalizationService _loc;
    private AppSettings _settings;
    private Action<AppSettings>? _onSaved;
    private bool _isRecordingHotkey;
    private List<BuiltInActionState> _builtInActionStates = new();

    private sealed class BuiltInActionState
    {
        public string ActionId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Icon { get; init; } = string.Empty;
        public string DefaultPrompt { get; init; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public bool CanEditPrompt { get; init; }
        public string EditPromptButtonText { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public string ToggleDeleteButtonText { get; set; } = "Delete";
        public double RowOpacity { get; set; } = 1.0;
    }

    public SettingsWindow(SettingsService settingsService, Action<AppSettings>? onSaved = null)
    {
        _settingsService = settingsService;
        _onSaved = onSaved;
        _settings = settingsService.Load();
        _loc = new LocalizationService(_settings.Language);

        InitializeComponent();
        ApplyLocalization();
        LoadSettings();

        if (!string.IsNullOrWhiteSpace(_settingsService.LastRecoveryMessage))
            StatusLabel.Text = _settingsService.LastRecoveryMessage;
    }

    private void LoadSettings()
    {
        // Provider
        ProviderCombo.SelectedIndex = _settings.ActiveProvider == "Groq" ? 1 : 0;

        // OpenRouter
        OpenRouterKeyBox.Password = _settings.OpenRouter.ApiKey;
        OpenRouterModelBox.Text = _settings.OpenRouter.Model;

        // Groq
        GroqKeyBox.Password = _settings.Groq.ApiKey;
        GroqModelBox.Text = _settings.Groq.Model;

        // Tavily
        TavilyKeyBox.Password = _settings.Tavily.ApiKey;

        // Hotkey
        HotkeyModifiers.Text = _settings.HotkeyModifiers;
        HotkeyKey.Text = _settings.HotkeyKey;
        _isRecordingHotkey = false;
        RecordHotkeyButton.Content = _loc.T("settings.recordShortcut", "Record Shortcut");

        // General
        StartWithWindowsCheck.IsChecked = StartupService.IsEnabled();
        SafeReplacePreviewCheck.IsChecked = _settings.SafeReplacePreviewMode;
        EnableDiagnosticsBundleCheck.IsChecked = _settings.EnableDiagnosticsBundle;

        // Writing profile
        WritingNameBox.Text = _settings.UserProfile?.Name ?? string.Empty;
        WritingEmailBox.Text = _settings.UserProfile?.Email ?? string.Empty;

        LanguageCombo.SelectedIndex = _settings.Language == "es" ? 1 : 0;

        // Version
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionLabel.Text = $"Spark v{v?.Major}.{v?.Minor}.{v?.Build ?? 0}";

        // Custom actions
        RefreshCustomActions();
        RefreshBuiltInActions();
    }

    private void RefreshCustomActions()
    {
        CustomActionsList.ItemsSource = null;
        CustomActionsList.ItemsSource = _settings.CustomActions;
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderCombo.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString() ?? "OpenRouter";
            OpenRouterPanel.Visibility = tag == "OpenRouter" ? Visibility.Visible : Visibility.Collapsed;
            GroqPanel.Visibility = tag == "Groq" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.OpenRouter.ApiKey = OpenRouterKeyBox.Password.Trim();
        _settings.OpenRouter.Model = OpenRouterModelBox.Text.Trim();
        _settings.Groq.ApiKey = GroqKeyBox.Password.Trim();
        _settings.Groq.Model = GroqModelBox.Text.Trim();
        _settings.Tavily.ApiKey = TavilyKeyBox.Password.Trim();
        _settings.HotkeyModifiers = HotkeyModifiers.Text.Trim();
        _settings.HotkeyKey = HotkeyKey.Text.Trim();
        _settings.UserProfile.Name = WritingNameBox.Text.Trim();
        _settings.UserProfile.Email = WritingEmailBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(_settings.HotkeyModifiers) || string.IsNullOrWhiteSpace(_settings.HotkeyKey))
        {
            StatusLabel.Text = _loc.T("settings.recordShortcutRequired", "Record a shortcut before saving.");
            return;
        }

        if (ProviderCombo.SelectedItem is ComboBoxItem item)
            _settings.ActiveProvider = item.Tag?.ToString() ?? "OpenRouter";

        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.SafeReplacePreviewMode = SafeReplacePreviewCheck.IsChecked == true;
        _settings.EnableDiagnosticsBundle = EnableDiagnosticsBundleCheck.IsChecked == true;

        if (LanguageCombo.SelectedItem is ComboBoxItem languageItem)
            _settings.Language = languageItem.Tag?.ToString() ?? "en";

        RebuildBuiltInPreferencesFromState();

        StartupService.Apply(_settings.StartWithWindows);

        _settingsService.Save(_settings);
        _onSaved?.Invoke(_settings);

        StatusLabel.Text = _loc.T("settings.saved", "✓ Settings saved");
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { StatusLabel.Text = ""; timer.Stop(); };
        timer.Start();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void NewAction_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomActionDialog();
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _settings.CustomActions.Add(dialog.Result);
            RefreshCustomActions();
        }
    }

    private void EditAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CustomAction action)
        {
            var dialog = new CustomActionDialog(action);
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                var idx = _settings.CustomActions.FindIndex(a => a.Id == action.Id);
                if (idx >= 0) _settings.CustomActions[idx] = dialog.Result;
                RefreshCustomActions();
            }
        }
    }

    private void DeleteAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CustomAction action)
        {
            _settings.CustomActions.RemoveAll(a => a.Id == action.Id);
            RefreshCustomActions();
        }
    }

    private void RefreshBuiltInActions()
    {
        var defaults = _builtInCatalog.GetDefaults();
        var preferenceMap = _builtInPreferences.BuildPreferenceMap(_settings.BuiltInActionPreferences);

        _builtInActionStates = defaults.Select(a =>
        {
            preferenceMap.TryGetValue(a.Id, out var preference);
            var isDeleted = preference?.IsDeleted == true;
            var effectivePrompt = string.IsNullOrWhiteSpace(preference?.PromptOverride)
                ? (a.Prompt ?? string.Empty)
                : preference!.PromptOverride.Trim();

            var state = new BuiltInActionState
            {
                ActionId = a.Id,
                Name = a.Name,
                Description = a.Description,
                Icon = _iconService.GetActionGlyph(a.Id, a.Icon),
                DefaultPrompt = a.Prompt ?? string.Empty,
                Prompt = effectivePrompt,
                CanEditPrompt = !string.IsNullOrWhiteSpace(a.Prompt),
                EditPromptButtonText = _loc.T("settings.builtInEditPrompt", "Edit Prompt"),
                IsDeleted = isDeleted
            };

            UpdateBuiltInStateView(state);
            return state;
        }).ToList();

        RebindBuiltInActionsList();
    }

    private void UpdateBuiltInStateView(BuiltInActionState state)
    {
        bool hasOverride = state.CanEditPrompt &&
                           !string.IsNullOrWhiteSpace(state.Prompt) &&
                           !string.Equals(state.Prompt.Trim(), state.DefaultPrompt, StringComparison.Ordinal);

        if (state.IsDeleted)
            state.StatusText = _loc.T("settings.builtInDeletedStatus", "Deleted from action panel");
        else if (hasOverride)
            state.StatusText = _loc.T("settings.builtInOverrideStatus", "Custom prompt override active");
        else
            state.StatusText = _loc.T("settings.builtInDefaultStatus", "Using default prompt");

        state.ToggleDeleteButtonText = state.IsDeleted
            ? _loc.T("settings.builtInRestore", "Restore")
            : _loc.T("settings.builtInDelete", "Delete");
        state.RowOpacity = state.IsDeleted ? 0.55 : 1.0;
    }

    private void RebindBuiltInActionsList()
    {
        BuiltInActionsList.ItemsSource = null;
        BuiltInActionsList.ItemsSource = _builtInActionStates;
    }

    private void EditBuiltInPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BuiltInActionState state)
            return;

        if (!state.CanEditPrompt)
            return;

        var dialog = new BuiltInPromptDialog(state.Name, state.Prompt, state.DefaultPrompt, _loc)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        state.Prompt = dialog.ResultPrompt;
        UpdateBuiltInStateView(state);
        RebindBuiltInActionsList();
    }

    private void ToggleBuiltInDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BuiltInActionState state)
            return;

        state.IsDeleted = !state.IsDeleted;
        UpdateBuiltInStateView(state);
        RebindBuiltInActionsList();
    }

    private void RestoreBuiltInDefaults_Click(object sender, RoutedEventArgs e)
    {
        _settings.BuiltInActionPreferences.Clear();
        RefreshBuiltInActions();
        StatusLabel.Text = _loc.T("settings.builtInRestoredPendingSave", "Built-in actions restored to defaults. Click Save to apply.");
    }

    private void RebuildBuiltInPreferencesFromState()
    {
        var preferences = new List<BuiltInActionPreference>();

        foreach (var state in _builtInActionStates)
        {
            var promptOverride = string.Empty;
            if (state.CanEditPrompt)
            {
                var currentPrompt = state.Prompt?.Trim() ?? string.Empty;
                var defaultPrompt = state.DefaultPrompt?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(currentPrompt) &&
                    !string.Equals(currentPrompt, defaultPrompt, StringComparison.Ordinal))
                {
                    promptOverride = currentPrompt;
                }
            }

            if (!state.IsDeleted && string.IsNullOrWhiteSpace(promptOverride))
                continue;

            preferences.Add(new BuiltInActionPreference
            {
                ActionId = state.ActionId,
                PromptOverride = promptOverride,
                IsDeleted = state.IsDeleted
            });
        }

        _settings.BuiltInActionPreferences = preferences;
    }

    private void RotateKeys_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.ClearStoredApiKeys();

        OpenRouterKeyBox.Password = string.Empty;
        GroqKeyBox.Password = string.Empty;
        TavilyKeyBox.Password = string.Empty;
        _settings.OpenRouter.ApiKey = string.Empty;
        _settings.Groq.ApiKey = string.Empty;
        _settings.Tavily.ApiKey = string.Empty;

        StatusLabel.Text = _loc.T("settings.keysCleared", "✓ Stored API keys cleared. Add new keys and click Save.");
    }

    private void ReleasesButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://www.spark-app.in/download-started")
            { UseShellExecute = true });
    }

    private void RecordHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = !_isRecordingHotkey;
        RecordHotkeyButton.Content = _isRecordingHotkey
            ? _loc.T("settings.recording", "Recording... Press shortcut")
            : _loc.T("settings.recordShortcut", "Record Shortcut");

        StatusLabel.Text = _isRecordingHotkey
            ? _loc.T("settings.recordingHint", "Press the key combination you want to use.")
            : string.Empty;
    }

    private void ClearHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        HotkeyModifiers.Text = string.Empty;
        HotkeyKey.Text = string.Empty;
        StatusLabel.Text = _loc.T("settings.shortcutCleared", "Shortcut cleared. Record a new one.");
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_isRecordingHotkey)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key))
        {
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var modifiersText = BuildModifiersText(modifiers, IsWindowsPressed());
        if (string.IsNullOrWhiteSpace(modifiersText))
        {
            StatusLabel.Text = _loc.T("settings.shortcutNeedsModifier", "Use at least one modifier key (Ctrl, Shift, Alt, or Win).");
            e.Handled = true;
            return;
        }

        var keyText = NormalizeHotkeyKey(key);
        if (string.IsNullOrWhiteSpace(keyText))
        {
            StatusLabel.Text = _loc.T("settings.shortcutUnsupportedKey", "Unsupported key. Try another key.");
            e.Handled = true;
            return;
        }

        HotkeyModifiers.Text = modifiersText;
        HotkeyKey.Text = keyText;

        _isRecordingHotkey = false;
        RecordHotkeyButton.Content = _loc.T("settings.recordShortcut", "Record Shortcut");
        StatusLabel.Text = _loc.T("settings.shortcutRecorded", "Shortcut recorded.");
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_isRecordingHotkey && e.Key == Key.Escape)
        {
            _isRecordingHotkey = false;
            RecordHotkeyButton.Content = _loc.T("settings.recordShortcut", "Record Shortcut");
            StatusLabel.Text = _loc.T("settings.recordingCancelled", "Shortcut recording cancelled.");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.96);
    }

    private void ApplyLocalization()
    {
        SettingsTitleText.Text = _loc.T("settings.title", "Settings");
        AiProviderHeaderText.Text = _loc.T("settings.aiProvider", "AI PROVIDER");
        ProviderLabelText.Text = _loc.T("settings.provider", "Provider");
        OpenRouterApiKeyLabelText.Text = _loc.T("settings.openrouterKey", "OpenRouter API Key");
        OpenRouterModelLabelText.Text = _loc.T("settings.modelOpenrouter", "Model (e.g. openai/gpt-4o-mini)");
        GroqApiKeyLabelText.Text = _loc.T("settings.groqKey", "Groq API Key");
        GroqModelLabelText.Text = _loc.T("settings.modelGroq", "Model (e.g. llama-3.3-70b-versatile)");
        ApiEncryptedNoteText.Text = _loc.T("settings.encryptedNote", "API keys are encrypted with Windows DPAPI.");
        RotateKeysButton.Content = _loc.T("settings.rotateKeys", "Rotate Keys");
        LanguageHeaderText.Text = _loc.T("settings.languageHeader", "LANGUAGE");
        LanguageLabelText.Text = _loc.T("settings.language", "Display Language");
        WritingProfileHeaderText.Text = _loc.T("settings.writingProfile", "WRITING PROFILE");
        WritingNameLabelText.Text = _loc.T("settings.writingName", "Your Name");
        WritingEmailLabelText.Text = _loc.T("settings.writingEmail", "Your Email (optional)");
        KeyboardShortcutHeaderText.Text = _loc.T("settings.hotkeyHeader", "KEYBOARD SHORTCUT");
        ModifiersLabelText.Text = _loc.T("settings.modifiers", "Modifiers (e.g. Ctrl+Shift)");
        KeyLabelText.Text = _loc.T("settings.key", "Key (e.g. Space)");
        GeneralHeaderText.Text = _loc.T("settings.general", "GENERAL");
        StartWithWindowsCheck.Content = _loc.T("settings.startWithWindows", "Start Spark automatically when Windows starts");
        SafeReplacePreviewCheck.Content = _loc.T("settings.safeReplacePreview", "Enable safe replace preview confirmation");
        EnableDiagnosticsBundleCheck.Content = _loc.T("settings.enableDiagnostics", "Enable diagnostic bundle generation");
        CustomActionsHeaderText.Text = _loc.T("settings.customActions", "CUSTOM ACTIONS");
        NewActionButton.Content = _loc.T("settings.newAction", "+ New Action");
        BuiltInActionsHeaderText.Text = _loc.T("settings.builtInActions", "BUILT-IN ACTIONS");
        RestoreBuiltInsButton.Content = _loc.T("settings.restoreBuiltInDefaults", "Restore Defaults");
        RecordHotkeyButton.Content = _loc.T("settings.recordShortcut", "Record Shortcut");
        ClearHotkeyButton.Content = _loc.T("settings.clearShortcut", "Clear");
        CancelButton.Content = _loc.T("common.cancel", "Cancel");
        SaveButton.Content = _loc.T("common.save", "Save");
    }

    private static bool IsModifierKey(Key key)
    {
        return key == Key.LeftCtrl || key == Key.RightCtrl ||
               key == Key.LeftShift || key == Key.RightShift ||
               key == Key.LeftAlt || key == Key.RightAlt ||
               key == Key.LWin || key == Key.RWin;
    }

    private static string BuildModifiersText(ModifierKeys modifiers, bool windowsPressed)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows) || windowsPressed) parts.Add("Win");
        return string.Join("+", parts);
    }

    private static bool IsWindowsPressed()
    {
        return GetAsyncKeyState(VK_LWIN) < 0 || GetAsyncKeyState(VK_RWIN) < 0;
    }

    private static string NormalizeHotkeyKey(Key key)
    {
        return key switch
        {
            Key.None => string.Empty,
            Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin => string.Empty,
            _ => key.ToString()
        };
    }
}
