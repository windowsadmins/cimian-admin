namespace CimianStudio.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

/// <summary>
/// AutoSuggestBox + chip list with X-to-remove. Used for any pkginfo / manifest field
/// that holds a small set of distinct strings (catalogs, requires, managed_installs, ...).
/// </summary>
public sealed partial class ChipListPicker : UserControl
{
    public event RoutedEventHandler? Changed;

    public string PlaceholderText
    {
        get => Picker.PlaceholderText;
        set => Picker.PlaceholderText = value;
    }

    private IReadOnlyList<string> _suggestions = [];

    public IReadOnlyList<string> Suggestions
    {
        get => _suggestions;
        set
        {
            _suggestions = value ?? [];
        }
    }

    public ChipListPicker()
    {
        InitializeComponent();
    }

    public void SetItems(IEnumerable<string>? items)
    {
        ChipsHost.Items.Clear();
        if (items is null)
        {
            return;
        }
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                ChipsHost.Items.Add(BuildChip(item));
            }
        }
    }

    public List<string> GetItems()
    {
        var list = new List<string>();
        foreach (var item in ChipsHost.Items)
        {
            if (item is FrameworkElement el && el.Tag is string name)
            {
                list.Add(name);
            }
        }
        return list;
    }

    private void OnPickerTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var query = sender.Text?.Trim() ?? string.Empty;
        var existing = new HashSet<string>(GetItems(), StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> matches = _suggestions.Where(n => !existing.Contains(n));
        if (!string.IsNullOrEmpty(query))
        {
            matches = matches.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        sender.ItemsSource = matches.Take(50).ToList();
    }

    private void OnPickerSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string name)
        {
            AddChip(name);
            sender.Text = string.Empty;
        }
    }

    private void OnPickerQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var name = (args.ChosenSuggestion as string) ?? args.QueryText?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            AddChip(name);
            sender.Text = string.Empty;
        }
    }

    private void OnChipsReordered(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        Changed?.Invoke(this, new RoutedEventArgs());
    }

    private void AddChip(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var existing = GetItems();
        if (existing.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        ChipsHost.Items.Add(BuildChip(name));
        Changed?.Invoke(this, new RoutedEventArgs());
    }

    private Border BuildChip(string name)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var onAccentText = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        stack.Children.Add(new TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = onAccentText,
        });

        var remove = new Button
        {
            Content = new FontIcon { FontSize = 10, Foreground = onAccentText, Glyph = "" },
            Padding = new Thickness(4),
            MinWidth = 22,
            MinHeight = 22,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorTertiaryBrush"],
            BorderThickness = new Thickness(0),
        };
        var border = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorTertiaryBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 4, 4),
            Tag = name,
            Child = stack,
        };
        remove.Click += (_, _) =>
        {
            ChipsHost.Items.Remove(border);
            Changed?.Invoke(this, new RoutedEventArgs());
        };
        stack.Children.Add(remove);
        return border;
    }
}
