namespace CimianStudio.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

/// <summary>
/// Chip list where each chip carries a "context" — top-level vs. one of the manifest's
/// conditional items. Lets the user move a managed item between contexts via a per-chip
/// dropdown without leaving the page.
/// </summary>
public sealed partial class ContextualChipList : UserControl
{
    public sealed record ContextOption(string Id, string Label, int IndentLevel);

    public sealed class ChipEntry
    {
        public string Name { get; set; } = string.Empty;
        public string ContextId { get; set; } = string.Empty;
    }

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
        set => _suggestions = value ?? [];
    }

    private IReadOnlyList<ContextOption> _contexts = [];
    private string _defaultContextId = string.Empty;

    public ContextualChipList()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the list of contexts (top-level + each conditional item) used to populate
    /// the per-chip dropdowns. The first context is treated as the default for new chips.
    /// </summary>
    public void SetContexts(IReadOnlyList<ContextOption> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        _contexts = contexts;
        _defaultContextId = contexts.Count > 0 ? contexts[0].Id : string.Empty;

        // Refresh the dropdown on any existing chips so they show the latest context list.
        foreach (var item in ChipsHost.Items)
        {
            if (item is FrameworkElement el && el.Tag is ChipEntry entry)
            {
                RefreshChipDropdown(el, entry);
            }
        }
    }

    public void SetItems(IEnumerable<ChipEntry>? items)
    {
        ChipsHost.Items.Clear();
        if (items is null) return;
        foreach (var entry in items)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)) continue;
            ChipsHost.Items.Add(BuildChip(entry));
        }
    }

    public List<ChipEntry> GetItems()
    {
        var list = new List<ChipEntry>();
        foreach (var item in ChipsHost.Items)
        {
            if (item is FrameworkElement el && el.Tag is ChipEntry entry)
            {
                list.Add(new ChipEntry { Name = entry.Name, ContextId = entry.ContextId });
            }
        }
        return list;
    }

    private void OnPickerTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        var query = sender.Text?.Trim() ?? string.Empty;
        var existing = new HashSet<string>(GetItems().Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
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
        // Fire Changed so the parent editor flips dirty when the user reorders rows.
        Changed?.Invoke(this, new RoutedEventArgs());
    }

    private void AddChip(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (GetItems().Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return;

        var entry = new ChipEntry { Name = name, ContextId = _defaultContextId };
        ChipsHost.Items.Add(BuildChip(entry));
        Changed?.Invoke(this, new RoutedEventArgs());
    }

    private Border BuildChip(ChipEntry entry)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Drag handle — visual affordance for reorder. The actual drag is handled by the
        // outer ListView via CanReorderItems / CanDragItems, but we surface the grip so
        // users know rows are reorderable.
        var dragHandle = new FontIcon
        {
            FontSize = 12,
            Glyph = "",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };
        Grid.SetColumn(dragHandle, 0);
        grid.Children.Add(dragHandle);

        var label = new TextBlock
        {
            Text = entry.Name,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        // Cap the context dropdown so a long conditional predicate (e.g.
        // `hostname CONTAINS "X" OR machine_model CONTAINS "Y" OR ...`) can't
        // push the chip wider than the row. MaxWidth + wrapping ItemTemplate
        // lets long predicates wrap to multiple lines inside the chip.
        var contextBox = new ComboBox
        {
            MinWidth = 180,
            MaxWidth = 360,
            VerticalAlignment = VerticalAlignment.Center,
            ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                """
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                              xmlns:opt="using:CimianStudio.Views">
                  <TextBlock Text="{Binding Label}"
                             TextWrapping="Wrap"
                             MaxWidth="320" />
                </DataTemplate>
                """),
        };
        Grid.SetColumn(contextBox, 2);
        grid.Children.Add(contextBox);

        var remove = new Button
        {
            Content = new FontIcon { FontSize = 10, Glyph = "" },
            Padding = new Thickness(4),
            MinWidth = 22,
            MinHeight = 22,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);

        var border = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorTertiaryBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 4, 4),
            Tag = entry,
            Child = grid,
        };

        contextBox.SelectionChanged += (_, _) =>
        {
            if (contextBox.SelectedItem is ContextOption sel)
            {
                entry.ContextId = sel.Id;
                Changed?.Invoke(this, new RoutedEventArgs());
            }
        };

        remove.Click += (_, _) =>
        {
            ChipsHost.Items.Remove(border);
            Changed?.Invoke(this, new RoutedEventArgs());
        };

        RefreshChipDropdown(border, entry);
        return border;
    }

    private void RefreshChipDropdown(FrameworkElement chip, ChipEntry entry)
    {
        if (chip is not Border border || border.Child is not Grid grid) return;
        var contextBox = grid.Children.OfType<ComboBox>().FirstOrDefault();
        if (contextBox is null) return;

        // Hide the dropdown entirely when there's nowhere meaningful to move the chip to
        // (i.e. only the implicit top-level context exists). This declutters manifests
        // that have no conditional_items.
        var hasChoices = _contexts.Count > 1;
        contextBox.Visibility = hasChoices ? Visibility.Visible : Visibility.Collapsed;

        if (!hasChoices)
        {
            // Force the chip to live at top-level so saves are unambiguous.
            entry.ContextId = _contexts.Count > 0 ? _contexts[0].Id : string.Empty;
            return;
        }

        contextBox.ItemsSource = _contexts;
        contextBox.DisplayMemberPath = nameof(ContextOption.Label);

        var match = _contexts.FirstOrDefault(c => string.Equals(c.Id, entry.ContextId, StringComparison.Ordinal));
        contextBox.SelectedItem = match ?? _contexts[0];
        if (match is null)
        {
            entry.ContextId = _contexts[0].Id;
        }
    }
}
