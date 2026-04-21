using System.Windows;
using Spark.Services;

namespace Spark.Windows;

public partial class BuiltInPromptDialog : Window
{
    private readonly string _defaultPrompt;
    private readonly LocalizationService _loc;

    public string ResultPrompt { get; private set; } = string.Empty;

    public BuiltInPromptDialog(string actionName, string currentPrompt, string defaultPrompt, LocalizationService loc)
    {
        InitializeComponent();

        _loc = loc;
        _defaultPrompt = defaultPrompt ?? string.Empty;
        ResultPrompt = currentPrompt ?? string.Empty;

        var titlePrefix = _loc.T("settings.builtInPromptDialogTitlePrefix", "Edit Prompt");
        var hint = _loc.T("settings.builtInPromptDialogHint", "Edit the built-in prompt. Use Reset to restore this action to its original prompt.");

        Title = titlePrefix;
        TitleText.Text = $"{titlePrefix}: {actionName}";
        HintText.Text = hint;
        ResetButton.Content = _loc.T("settings.builtInPromptDialogReset", "Reset");
        CancelButton.Content = _loc.T("common.cancel", "Cancel");
        SaveButton.Content = _loc.T("common.save", "Save");

        PromptBox.Text = ResultPrompt;
        PromptBox.Select(PromptBox.Text.Length, 0);
        PromptBox.Focus();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        PromptBox.Text = _defaultPrompt;
        PromptBox.Focus();
        PromptBox.Select(PromptBox.Text.Length, 0);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var prompt = (PromptBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            MessageBox.Show(
                _loc.T("settings.promptCannotBeEmpty", "Prompt cannot be empty."),
                "Spark",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ResultPrompt = prompt;
        DialogResult = true;
        Close();
    }
}
