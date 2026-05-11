namespace CimianAdmin.Views;

using System.Globalization;
using CimianAdmin.Core.Models.Catalogs;
using CimianAdmin.Core.Models.Packages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class CatalogCompareDialog : ContentDialog
{
    private IReadOnlyList<Catalog> _catalogs = [];

    public CatalogCompareDialog()
    {
        InitializeComponent();
    }

    public void SetCatalogs(IReadOnlyList<Catalog> catalogs, Catalog? preferredA = null, Catalog? preferredB = null)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        _catalogs = catalogs;

        var names = catalogs.Select(c => c.Name).ToList();
        CatalogABox.ItemsSource = names;
        CatalogBBox.ItemsSource = names;

        // Smart defaults: prefer Production for B, Staging for A so the diff reads as
        // "what's queued in Staging that hasn't promoted to Production yet".
        CatalogABox.SelectedItem = preferredA?.Name
            ?? names.FirstOrDefault(n => string.Equals(n, "Staging", StringComparison.OrdinalIgnoreCase))
            ?? names.FirstOrDefault();
        CatalogBBox.SelectedItem = preferredB?.Name
            ?? names.FirstOrDefault(n => string.Equals(n, "Production", StringComparison.OrdinalIgnoreCase))
            ?? names.Skip(1).FirstOrDefault()
            ?? names.FirstOrDefault();

        Recompute();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Recompute();

    private void Recompute()
    {
        var a = FindCatalog(CatalogABox.SelectedItem as string);
        var b = FindCatalog(CatalogBBox.SelectedItem as string);

        if (a is null || b is null)
        {
            OnlyAList.ItemsSource = null;
            OnlyBList.ItemsSource = null;
            BothList.ItemsSource = null;
            return;
        }

        var aNames = new HashSet<string>(a.Packages.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var bNames = new HashSet<string>(b.Packages.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        var onlyA = a.Packages
            .Where(p => !bNames.Contains(p.Name))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var onlyB = b.Packages
            .Where(p => !aNames.Contains(p.Name))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var both = a.Packages
            .Where(p => bNames.Contains(p.Name))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        OnlyAList.ItemsSource = onlyA;
        OnlyBList.ItemsSource = onlyB;
        BothList.ItemsSource = both;

        OnlyAHeader.Text = $"Only in {a.Name} ({onlyA.Count.ToString(CultureInfo.InvariantCulture)})";
        OnlyBHeader.Text = $"Only in {b.Name} ({onlyB.Count.ToString(CultureInfo.InvariantCulture)})";
        BothHeader.Text = $"In both ({both.Count.ToString(CultureInfo.InvariantCulture)})";
    }

    private Catalog? FindCatalog(string? name) =>
        string.IsNullOrEmpty(name)
            ? null
            : _catalogs.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private void OnPackageDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not Package package) return;
        App.Resolve<PackageEditorWindow>().Open(package);
    }
}
