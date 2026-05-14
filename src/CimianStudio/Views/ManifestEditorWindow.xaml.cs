namespace CimianStudio.Views;

using CimianStudio.Core.Models.Manifests;
using Microsoft.UI.Xaml;

public sealed partial class ManifestEditorWindow : Window
{
    public ManifestEditorWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    public void Open(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var title = manifest.Name ?? manifest.DisplayName ?? "Manifest";
        Title = title;
        AppTitleText.Text = title;
        Editor.SetManifest(manifest);
        Activate();
    }
}
