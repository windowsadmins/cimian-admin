namespace CimianAdmin.Views;

using CimianAdmin.Core.Models.Predicates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

/// <summary>
/// MunkiAdmin-style predicate editor. Two tabs: "Predicate Editor" gives the
/// keypath/operator/value rows with +/- buttons; "Custom" is a freeform text editor.
/// On open, parses the incoming predicate string and falls back to the Custom tab
/// when the predicate doesn't fit the simple flat AND/OR model the UI supports.
/// </summary>
public sealed partial class PredicateEditorDialog : ContentDialog
{
    private PredicateBuilder _builder = new();
    private bool _suppressEvents;

    /// <summary>The final predicate string, available after <c>ShowAsync()</c>.</summary>
    public string Predicate { get; private set; } = string.Empty;

    public PredicateEditorDialog(string? initial)
    {
        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(initial) && PredicateParser.TryParse(initial, out var parsed))
        {
            _builder = parsed;
        }
        else
        {
            _builder = new PredicateBuilder();
            CustomTextBox.Text = initial ?? string.Empty;
            ShowCustomTab(force: !string.IsNullOrWhiteSpace(initial));
        }

        RenderBuilder();
        PrimaryButtonClick += OnPrimary;
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (CustomTab.Visibility == Visibility.Visible)
        {
            Predicate = CustomTextBox.Text?.Trim() ?? string.Empty;
        }
        else
        {
            Predicate = PredicateSerializer.ToPredicateString(_builder);
        }
    }

    private void OnPredicateTabClicked(object sender, RoutedEventArgs e) => ShowPredicateTab();
    private void OnCustomTabClicked(object sender, RoutedEventArgs e) => ShowCustomTab();

    private void ShowPredicateTab()
    {
        PredicateTabButton.IsChecked = true;
        CustomTabButton.IsChecked = false;
        PredicateTab.Visibility = Visibility.Visible;
        CustomTab.Visibility = Visibility.Collapsed;

        // Sync from Custom → Predicate if the text actually parses; otherwise keep
        // the builder as-is so the user doesn't lose work.
        if (PredicateParser.TryParse(CustomTextBox.Text ?? string.Empty, out var parsed))
        {
            _builder = parsed;
            RenderBuilder();
        }
    }

    private void ShowCustomTab(bool force = false)
    {
        PredicateTabButton.IsChecked = false;
        CustomTabButton.IsChecked = true;
        PredicateTab.Visibility = Visibility.Collapsed;
        CustomTab.Visibility = Visibility.Visible;

        // Sync from Predicate → Custom by emitting the canonical string.
        if (!force)
        {
            CustomTextBox.Text = PredicateSerializer.ToPredicateString(_builder);
        }
    }

    private void OnCompoundChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (CompoundPicker.SelectedItem is ComboBoxItem item && item.Tag is string tag &&
            Enum.TryParse<CompoundType>(tag, out var c))
        {
            _builder.Compound = c;
        }
    }

    private void RenderBuilder()
    {
        _suppressEvents = true;
        try
        {
            CompoundPicker.SelectedIndex = _builder.Compound switch
            {
                CompoundType.Any => 1,
                CompoundType.None => 2,
                _ => 0,
            };
        }
        finally
        {
            _suppressEvents = false;
        }

        // Ensure at least one empty row so the user has something to fill in.
        if (_builder.Rows.Count == 0)
        {
            _builder.Rows.Add(new PredicateRow
            {
                Keypath = PredicateKeypaths.All[0].Key,
                OperatorToken = "==",
                Value = string.Empty,
            });
        }

        RowsHost.Children.Clear();
        foreach (var row in _builder.Rows)
        {
            RowsHost.Children.Add(BuildRowControl(row));
        }
    }

    private Grid BuildRowControl(PredicateRow row)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var keypathBox = new ComboBox
        {
            ItemsSource = PredicateKeypaths.All.Select(k => k.Label).ToList(),
            SelectedIndex = Math.Max(0, IndexOfKeypath(row.Keypath)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(keypathBox, 0);

        var operatorBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(operatorBox, 1);

        var valueBox = new TextBox
        {
            Text = row.Value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(valueBox, 2);

        var removeButton = new Button
        {
            Content = "−",
            Width = 32,
        };
        Grid.SetColumn(removeButton, 3);
        removeButton.Click += (_, _) =>
        {
            if (_builder.Rows.Count <= 1) return;
            _builder.Rows.Remove(row);
            RenderBuilder();
        };

        var addButton = new Button
        {
            Content = "+",
            Width = 32,
        };
        Grid.SetColumn(addButton, 4);
        addButton.Click += (_, _) =>
        {
            var idx = _builder.Rows.IndexOf(row);
            _builder.Rows.Insert(idx + 1, new PredicateRow
            {
                Keypath = PredicateKeypaths.All[0].Key,
                OperatorToken = "==",
                Value = string.Empty,
            });
            RenderBuilder();
        };

        keypathBox.SelectionChanged += (_, _) =>
        {
            var idx = keypathBox.SelectedIndex;
            if (idx < 0 || idx >= PredicateKeypaths.All.Count) return;
            var kp = PredicateKeypaths.All[idx];
            row.Keypath = kp.Key;
            RefreshOperatorsForRow(operatorBox, kp.ValueType, row);
        };

        operatorBox.SelectionChanged += (_, _) =>
        {
            if (operatorBox.SelectedItem is PredicateOperator op) row.OperatorToken = op.Token;
        };

        valueBox.TextChanged += (_, _) => row.Value = valueBox.Text;

        // Initial operator list for the row's current keypath.
        var currentKp = PredicateKeypaths.Find(row.Keypath) ?? PredicateKeypaths.All[0];
        RefreshOperatorsForRow(operatorBox, currentKp.ValueType, row);

        grid.Children.Add(keypathBox);
        grid.Children.Add(operatorBox);
        grid.Children.Add(valueBox);
        grid.Children.Add(removeButton);
        grid.Children.Add(addButton);
        return grid;
    }

    private static void RefreshOperatorsForRow(ComboBox box, PredicateValueType type, PredicateRow row)
    {
        var operators = PredicateOperators.For(type);
        box.ItemsSource = operators;
        box.DisplayMemberPath = nameof(PredicateOperator.Label);

        var matchIndex = -1;
        for (var i = 0; i < operators.Count; i++)
        {
            if (string.Equals(operators[i].Token, row.OperatorToken, StringComparison.OrdinalIgnoreCase))
            {
                matchIndex = i;
                break;
            }
        }
        if (matchIndex < 0)
        {
            matchIndex = 0;
            row.OperatorToken = operators[0].Token;
        }
        box.SelectedIndex = matchIndex;
    }

    private static int IndexOfKeypath(string key)
    {
        for (var i = 0; i < PredicateKeypaths.All.Count; i++)
        {
            if (string.Equals(PredicateKeypaths.All[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
}
