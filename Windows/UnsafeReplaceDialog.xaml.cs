using System.Windows;
using System.Windows.Input;
using Spark.Services;

namespace Spark.Windows;

public enum UnsafeReplaceDecision
{
    ReplaceAnyway,
    CopyOnly,
    Cancel
}

public partial class UnsafeReplaceDialog : Window
{
    public UnsafeReplaceDecision Decision { get; private set; } = UnsafeReplaceDecision.Cancel;
    public bool DontAskAgainForSession { get; private set; }

    public UnsafeReplaceDialog(string sourceProcess, string currentProcess, bool allowCancel)
    {
        InitializeComponent();

        DetailsText.Text =
            "Focus changed since capture.\n\n" +
            $"Captured in: {sourceProcess}\n" +
            $"Current app: {currentProcess}";

        CancelButton.Visibility = allowCancel ? Visibility.Visible : Visibility.Collapsed;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowGpuAnimationService.ResetOpenState(RootBorder, ScaleT, 0.94, TranslateT, 8);
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.94, TranslateT, 8, 180);
    }

    private void ReplaceAnyway_Click(object sender, RoutedEventArgs e)
    {
        SetResult(UnsafeReplaceDecision.ReplaceAnyway);
    }

    private void CopyOnly_Click(object sender, RoutedEventArgs e)
    {
        SetResult(UnsafeReplaceDecision.CopyOnly);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        SetResult(UnsafeReplaceDecision.Cancel);
    }

    private void SetResult(UnsafeReplaceDecision decision)
    {
        Decision = decision;
        DontAskAgainForSession = DontAskAgainCheck.IsChecked == true;
        DialogResult = true;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SetResult(UnsafeReplaceDecision.Cancel);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
