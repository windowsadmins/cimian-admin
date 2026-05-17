namespace CimianStudio.Views;

using CimianStudio.Core.Models.Search;
using Microsoft.UI.Xaml.Controls;

public sealed partial class ManifestSearchDialog : ContentDialog
{
    private static readonly (ManifestSearchField Value, string Label)[] FieldOptions =
    [
        (ManifestSearchField.Name, "Name"),
        (ManifestSearchField.DisplayName, "Display name"),
        (ManifestSearchField.Notes, "Notes"),
        (ManifestSearchField.FilePath, "File path"),
        (ManifestSearchField.Catalog, "Catalog"),
        (ManifestSearchField.ManagedInstall, "Managed install"),
        (ManifestSearchField.ManagedUninstall, "Managed uninstall"),
        (ManifestSearchField.ManagedUpdate, "Managed update"),
        (ManifestSearchField.OptionalInstall, "Optional install"),
        (ManifestSearchField.DefaultInstall, "Default install"),
        (ManifestSearchField.IncludedManifest, "Included manifest"),
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

    public ManifestSearchPredicate? Result { get; private set; }

    public ManifestSearchDialog(ManifestSearchPredicate? initial)
    {
        InitializeComponent();

        var seed = initial is null || initial.IsEmpty
            ? new ManifestSearchPredicate { Rules = [new ManifestSearchRule()] }
            : Clone(initial);

        MatchModeCombo.SelectedIndex = seed.MatchAll ? 0 : 1;
        foreach (var rule in seed.Rules)
        {
            RowsHost.Children.Add(BuildRow(rule));
        }

        PrimaryButtonClick += OnApplyClick;
        SecondaryButtonClick += OnClearClick;
    }

    private static ManifestSearchPredicate Clone(ManifestSearchPredicate src) => new()
    {
        MatchAll = src.MatchAll,
        Rules = [.. src.Rules.Select(r => new ManifestSearchRule { Field = r.Field, Op = r.Op, Value = r.Value })],
    };

    private void OnAddRuleClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        RowsHost.Children.Add(BuildRow(new ManifestSearchRule()));
    }

    private Grid BuildRow(ManifestSearchRule rule)
    {
        var row = new Grid { ColumnSpacing = 6, Tag = rule };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });

        var fieldBox = new ComboBox { HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
        foreach (var (v, label) in FieldOptions)
        {
            fieldBox.Items.Add(new ComboBoxItem { Content = label, Tag = v });
        }
        fieldBox.SelectedIndex = Array.FindIndex(FieldOptions, o => o.Value == rule.Field);
        Grid.SetColumn(fieldBox, 0);
        row.Children.Add(fieldBox);

        var opBox = new ComboBox { HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
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
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            PlaceholderText = "value",
        };
        Grid.SetColumn(valueBox, 2);
        row.Children.Add(valueBox);

        opBox.SelectionChanged += (_, _) =>
        {
            var picked = (opBox.SelectedItem as ComboBoxItem)?.Tag is SmartSearchOperator op
                ? op
                : SmartSearchOperator.Contains;
            valueBox.IsEnabled = picked != SmartSearchOperator.IsEmpty && picked != SmartSearchOperator.IsNotEmpty;
        };
        valueBox.IsEnabled = rule.Op != SmartSearchOperator.IsEmpty && rule.Op != SmartSearchOperator.IsNotEmpty;

        var removeBtn = new Button { Content = "Remove" };
        removeBtn.Click += (_, _) => RowsHost.Children.Remove(row);
        Grid.SetColumn(removeBtn, 3);
        row.Children.Add(removeBtn);

        return row;
    }

    private void OnApplyClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var pred = new ManifestSearchPredicate
        {
            MatchAll = (MatchModeCombo.SelectedItem as ComboBoxItem)?.Tag is not string tag || string.Equals(tag, "All", StringComparison.Ordinal),
        };

        foreach (var child in RowsHost.Children)
        {
            if (child is not Grid row) continue;
            var fieldBox = (ComboBox)row.Children[0];
            var opBox = (ComboBox)row.Children[1];
            var valueBox = (TextBox)row.Children[2];

            var field = (fieldBox.SelectedItem as ComboBoxItem)?.Tag is ManifestSearchField a
                ? a : ManifestSearchField.Name;
            var op = (opBox.SelectedItem as ComboBoxItem)?.Tag is SmartSearchOperator o
                ? o : SmartSearchOperator.Contains;
            var value = valueBox.Text?.Trim() ?? string.Empty;

            var needsValue = op != SmartSearchOperator.IsEmpty && op != SmartSearchOperator.IsNotEmpty;
            if (needsValue && string.IsNullOrEmpty(value)) continue;

            pred.Rules.Add(new ManifestSearchRule { Field = field, Op = op, Value = value });
        }

        Result = pred;
    }

    private void OnClearClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = new ManifestSearchPredicate();
    }
}
