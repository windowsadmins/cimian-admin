namespace CimianAdmin.Views;

using CimianAdmin.Core.Models.Packages;
using Microsoft.UI.Xaml;

public sealed partial class PackageEditorWindow : Window
{
    public PackageEditorWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    public void Open(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Title = package.EffectiveDisplayName;
        AppTitleText.Text = package.EffectiveDisplayName;
        Editor.SetPackage(package);
        Activate();
    }
}
