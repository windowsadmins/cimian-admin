namespace CimianAdmin.Views;

using CimianAdmin.Core.Models.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class SmartSearchDialog : ContentDialog
{
    private static readonly (SmartSearchField Value, string Label)[] AttributeOptions =
    [
        (SmartSearchField.Name, "Name"),
        (SmartSearchField.DisplayName, "Display name"),
        (SmartSearchField.Version, "Version"),
        (SmartSearchField.Category, "Category"),
        (SmartSearchField.Developer, "Developer"),
        (SmartSearchField.Description, "Description"),
        (SmartSearchField.Catalog, "Catalog"),
        (SmartSearchField.InstallerType, "Installer type"),
        (SmartSearchField.Requires, "Requires"),
        (SmartSearchField.BlockingApplication, "Blocking app"),
        (SmartSearchField.SupportedArchitecture, "Architecture"),
    ];

    private static readonly (SmartSearchOperator Value, string Label)[] OperatorOptions =
    [
        (SmartSearchOperator.Contains, "contains"),
        (SmartSearchOperator.DoesNotContain, "does not contain"),
        (SmartSearchOperator.Equals, "equals"),
        (SmartSearchOperator.StartsWith, "starts with"),
        (SmartSearchOperator.EndsWith, "ends with"),
        (SmartSearchOperator.IsEmpty, "is empty"),
        (SmartSearchOperator.IsNotEmpty, "is not empty"),
    ];

    /// <summary>Result. Null if the user cancelled. Empty predicate signals "clear".</summary>
    public SmartSearchPredicate? Result { get; private set; }

    public SmartSearchDialog(SmartSearchPredicate? initial)
    {
        InitializeComponent();

        var seed = initial is null || initial.IsEmpty
            ? new SmartSearchPredicate { Rules = [new SmartSearchRule()] }
            : Clone(initial);

        MatchModeCombo.SelectedIndex = seed.MatchAll ? 0 : 1;
        foreach (var rule in seed.Rules)
        {
            RowsHost.Children.Add(BuildRow(rule));
        }

        PrimaryButtonClick += OnApplyClick;
        SecondaryButtonClick += OnClearClick;
    }

    private static SmartSearchPredicate Clone(SmartSearchPredicate src) => new()
    {
        MatchAll = src.MatchAll,
        Rules = [.. src.Rules.Select(r => new SmartSearchRule { Field = r.Field, Op = r.Op, Value = r.Value })],
    };

    private void OnMatchModeChanged(object sender, SelectionChangedEventArgs e)
    {
        // Live preview not needed — value applied on PrimaryButtonClick.
    }

    private void OnAddRuleClicked(object sender, RoutedEventArgs e)
    {
        RowsHost.Children.Add(BuildRow(new SmartSearchRule()));
    }

    private Grid BuildRow(SmartSearchRule rule)
    {
        var row = new Grid
        {
            ColumnSpacing = 6,
            Tag = rule,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var attrBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (v, label) in AttributeOptions)
        {
            attrBox.Items.Add(new ComboBoxItem { Content = label, Tag = v });
        }
        attrBox.SelectedIndex = Array.FindIndex(AttributeOptions, o => o.Value == rule.Field);
        Grid.SetColumn(attrBox, 0);
        row.Children.Add(attrBox);

        var opBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (v, label) in OperatorOptions)
        {
            opBox.Items.Add(new ComboBoxItem { Content = label, Tag = v });
        }
        opBox.SelectedIndex = Array.FindIndex(OperatorOptions, o => o.Value == rule.Op);
        Grid.SetColumn(opBox, 1);
        row.Children.Add(opBox);

        var valueBox = new TextBox
        {
            Text = rule.Value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "value",
        };
        Grid.SetColumn(valueBox, 2);
        row.Children.Add(valueBox);

        // is-empty / is-not-empty don't need a value — disable the box so it's
        // visually obvious the input is ignored.
        opBox.SelectionChanged += (_, _) =>
        {
            var picked = (opBox.SelectedItem as ComboBoxItem)?.Tag is SmartSearchOperator op
                ? op
                : SmartSearchOperator.Contains;
            valueBox.IsEnabled = picked != SmartSearchOperator.IsEmpty && picked != SmartSearchOperator.IsNotEmpty;
        };
        // Fire once to set initial enabled state.
        valueBox.IsEnabled = rule.Op != SmartSearchOperator.IsEmpty && rule.Op != SmartSearchOperator.IsNotEmpty;

        var removeBtn = new Button { Content = "Remove" };
        removeBtn.Click += (_, _) =>
        {
            RowsHost.Children.Remove(row);
        };
        Grid.SetColumn(removeBtn, 3);
        row.Children.Add(removeBtn);

        return row;
    }

    private void OnApplyClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var pred = new SmartSearchPredicate
        {
            MatchAll = (MatchModeCombo.SelectedItem as ComboBoxItem)?.Tag is not string tag || string.Equals(tag, "All", StringComparison.Ordinal),
        };

        foreach (var child in RowsHost.Children)
        {
            if (child is not Grid row) continue;
            var attrBox = (ComboBox)row.Children[0];
            var opBox = (ComboBox)row.Children[1];
            var valueBox = (TextBox)row.Children[2];

            var field = (attrBox.SelectedItem as ComboBoxItem)?.Tag is SmartSearchField a
                ? a : SmartSearchField.Name;
            var op = (opBox.SelectedItem as ComboBoxItem)?.Tag is SmartSearchOperator o
                ? o : SmartSearchOperator.Contains;
            var value = valueBox.Text?.Trim() ?? string.Empty;

            // Skip empty-value rules for text-needing operators — keeping them
            // would match everything (Contains "") or nothing (Equals "").
            var needsValue = op != SmartSearchOperator.IsEmpty && op != SmartSearchOperator.IsNotEmpty;
            if (needsValue && string.IsNullOrEmpty(value)) continue;

            pred.Rules.Add(new SmartSearchRule { Field = field, Op = op, Value = value });
        }

        Result = pred;
    }

    private void OnClearClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = new SmartSearchPredicate();
    }
}
