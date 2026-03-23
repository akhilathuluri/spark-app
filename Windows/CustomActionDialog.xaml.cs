using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spark.Models;
using Spark.Services;

namespace Spark.Windows;

public partial class CustomActionDialog : Window
{
    private readonly IconService _iconService = new();
    public CustomAction? Result { get; private set; }
    private readonly Guid _existingId;

    public CustomActionDialog(CustomAction? existing = null)
    {
        InitializeComponent();
        BuildIconPicker();

        if (existing != null)
        {
            _existingId = existing.Id;
            IconBox.Text = _iconService.NormalizeCustomActionIcon(existing.Icon);
            NameBox.Text = existing.Name;
            DescBox.Text = existing.Description;
            InstructionsBox.Text = existing.Instructions;
        }
        else
        {
            _existingId = Guid.NewGuid();
            IconBox.Text = _iconService.GetGlyph(IconKey.CustomAction);
        }

        Loaded += OnLoaded;
        IconBox.TextChanged += (_, _) => UpdatePickerSelection();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowGpuAnimationService.ResetOpenState(RootBorder, ScaleT, 0.94, TranslateT, 8);
        WindowGpuAnimationService.AnimateOpen(RootBorder, ScaleT, 0.94, TranslateT, 8);
        UpdatePickerSelection();
    }

    private void BuildIconPicker()
    {
        IconPickerPanel.Children.Clear();

        foreach (var glyph in _iconService.GetCustomActionPickerGlyphs())
        {
            var btn = new Button
            {
                Content = glyph,
                Tag = glyph,
                Style = (Style)FindResource("PickerButtonStyle")
            };
            btn.Click += IconPicker_Click;
            IconPickerPanel.Children.Add(btn);
        }
    }

    private void IconPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string glyph)
            return;

        IconBox.Text = glyph;
    }

    private void UpdatePickerSelection()
    {
        var selected = _iconService.NormalizeCustomActionIcon(IconBox.Text);

        foreach (var child in IconPickerPanel.Children)
        {
            if (child is not Button btn || btn.Tag is not string glyph)
                continue;

            if (string.Equals(glyph, selected, StringComparison.Ordinal))
            {
                btn.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 106, 247));
                btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 58));
            }
            else
            {
                btn.ClearValue(BackgroundProperty);
                btn.ClearValue(BorderBrushProperty);
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(InstructionsBox.Text))
        {
            MessageBox.Show("Name and Instructions are required.", "Spark", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new CustomAction
        {
            Id = _existingId,
            Icon = _iconService.NormalizeCustomActionIcon(IconBox.Text),
            Name = NameBox.Text.Trim(),
            Description = DescBox.Text.Trim(),
            Instructions = InstructionsBox.Text.Trim()
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
