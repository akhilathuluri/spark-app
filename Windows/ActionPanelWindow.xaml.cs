using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Spark.Models;
using Spark.Services;
using System.Windows.Markup;
using System.Windows.Data;

namespace Spark.Windows;

// Converter for showing shortcut badge only when not empty
[ValueConversion(typeof(string), typeof(Visibility))]
public class StringToVisibilityConverter : MarkupExtension, System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public partial class ActionPanelWindow : Window
{
    private readonly IconService _iconService = new();
    private readonly ActionsService _actionsService;
    private readonly AiService _aiService;
    private readonly TextCaptureService _textCapture;
    private readonly SettingsService _settingsService;
    private readonly ResultWindow _resultWindow;
    private List<ActionItem> _allActions = new();
    private string _selectedText = string.Empty;
    private System.Windows.Point _cursorPos;

    // Guard: prevents Deactivated from closing right as the window appears
    private bool _allowDeactivateClose = false;

    public ActionPanelWindow(
        ActionsService actionsService,
        AiService aiService,
        TextCaptureService textCapture,
        SettingsService settingsService,
        ResultWindow resultWindow)
    {
        _actionsService = actionsService;
        _aiService = aiService;
        _textCapture = textCapture;
        _settingsService = settingsService;
        _resultWindow = resultWindow;

        InitializeComponent();
        SearchIconText.Text = _iconService.GetGlyph(IconKey.Search);

        // Only close on deactivation after the guard period has elapsed
        Deactivated += (_, _) => { if (_allowDeactivateClose) AnimateClose(); };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => SearchBox.Focus();

    public void ShowWithText(string selectedText, System.Windows.Point cursorPos)
    {
        _selectedText = selectedText;
        _cursorPos    = cursorPos;
        _allActions   = _actionsService.GetSmartSuggestions(selectedText);

        var settings = _settingsService.Load();
        ProviderLabel.Text = settings.ActiveProvider;

        RefreshList(string.Empty);
        PositionNearCursor(cursorPos);

        SearchBox.Text = string.Empty;

        // Lock out Deactivated-close until all activation/focus events settle
        _allowDeactivateClose = false;

        Show();
        Activate();
        SearchBox.Focus();

        // AnimateOpen every time — Loaded only fires once, so this handles repeated show/hide cycles
        AnimateOpen();

        // Allow deactivation-close only after the dispatcher has fully processed all focus events
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => _allowDeactivateClose = true));
    }

    private void PositionNearCursor(System.Windows.Point cursor)
    {
        // Get screen working area
        var screen = SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this);

        double x = cursor.X / dpi.DpiScaleX;
        double y = cursor.Y / dpi.DpiScaleY;

        // Offset slightly from cursor
        x += 12;
        y += 12;

        // Keep on screen — use ActualHeight instead of magic number
        double estimatedHeight = 450; // approximation if ActualHeight not ready yet
        if (ActualHeight > 0) estimatedHeight = ActualHeight;

        if (x + ActualWidth > screen.Right) x = screen.Right - ActualWidth - 10;
        if (y + estimatedHeight > screen.Bottom) y = y - estimatedHeight - 20;
        if (x < screen.Left) x = screen.Left + 10;
        if (y < screen.Top) y = screen.Top + 10;

        Left = x;
        Top = y;
    }

    private void RefreshList(string filter)
    {
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? _allActions
            : _allActions.Where(a =>
                a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        ActionsList.ItemsSource = filtered;

        if (filtered.Count > 0)
            ActionsList.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshList(SearchBox.Text);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                AnimateClose();
                e.Handled = true;
                break;

            case Key.Down:
                if (ActionsList.Items.Count > 0)
                {
                    ActionsList.SelectedIndex = Math.Min(ActionsList.SelectedIndex + 1, ActionsList.Items.Count - 1);
                    ActionsList.ScrollIntoView(ActionsList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (ActionsList.Items.Count > 0)
                {
                    ActionsList.SelectedIndex = Math.Max(ActionsList.SelectedIndex - 1, 0);
                    ActionsList.ScrollIntoView(ActionsList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Enter:
                ExecuteSelected();
                e.Handled = true;
                break;
        }
    }

    private void ActionsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ExecuteSelected(); e.Handled = true; }
        if (e.Key == Key.Escape) { AnimateClose(); e.Handled = true; }
    }

    private void ActionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelected();
    }

    // Single-click on a list item executes that action
    private void ActionsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Only fire if the click landed on an actual item container (not scrollbar / padding)
        if (e.OriginalSource is FrameworkElement fe &&
            ItemsControl.ContainerFromElement(ActionsList, fe) is ListBoxItem)
        {
            ExecuteSelected();
        }
    }

    private void ActionsList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void ExecuteSelected()
    {
        if (ActionsList.SelectedItem is not ActionItem action) return;

        // "Ask AI" opens a custom-prompt input window instead of going straight to results
        if (action.Id == "ask_ai")
        {
            var selectedText = _selectedText;
            var cursorPos    = _cursorPos;
            AnimateClose(onComplete: () =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var askWindow = new AskWindow(_aiService, _textCapture, _settingsService, _resultWindow);
                    askWindow.ShowWithText(selectedText, cursorPos);
                }));
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(action.Prompt)) return;

        AnimateClose(onComplete: () =>
        {
            // Defer to next dispatcher frame so we're fully outside the animation callback
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _resultWindow.ShowWithProcessing(action, _selectedText);
            }));
        });
    }

    // Window-level preview handler: catches keys regardless of which child has focus.
    // This makes arrow navigation + Enter work even when focus drifts to the ListBox.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                AnimateClose();
                e.Handled = true;
                break;

            case Key.Enter:
                ExecuteSelected();
                e.Handled = true;
                break;

            case Key.Down:
                if (ActionsList.Items.Count > 0)
                {
                    ActionsList.SelectedIndex = Math.Min(ActionsList.SelectedIndex + 1, ActionsList.Items.Count - 1);
                    ActionsList.ScrollIntoView(ActionsList.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (ActionsList.Items.Count > 0)
                {
                    ActionsList.SelectedIndex = Math.Max(ActionsList.SelectedIndex - 1, 0);
                    ActionsList.ScrollIntoView(ActionsList.SelectedItem);
                }
                e.Handled = true;
                break;
        }
        base.OnPreviewKeyDown(e);
    }

    // ── Animations ──────────────────────────────────────────
    private void AnimateOpen()
    {
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.92, TranslateT, 8);
    }

    private void AnimateClose(Action? onComplete = null)
    {
        if (!IsVisible) { onComplete?.Invoke(); return; }

        _allowDeactivateClose = false; // prevent Deactivated re-entry during close

        var dur = new Duration(TimeSpan.FromMilliseconds(160));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var opacityAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opacityAnim.Completed += (_, _) =>
        {
            Hide();
            // Clear animation holds so XAML base values (Opacity=0, Scale=0.92, Y=8) restore cleanly
            // This ensures AnimateOpen starts from the correct XAML baseline on the next show
            RootBorder.BeginAnimation(OpacityProperty, null);
            ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            TranslateT.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            onComplete?.Invoke();
        };
        RootBorder.BeginAnimation(OpacityProperty, opacityAnim);

        var scaleAnim = new DoubleAnimation(1.0, 0.94, dur) { EasingFunction = ease };
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnim);
        ScaleT.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnim);
    }
}
