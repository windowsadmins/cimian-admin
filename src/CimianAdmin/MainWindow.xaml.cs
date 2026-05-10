namespace CimianAdmin;

using System;
using System.IO;
using System.Threading.Tasks;
using CimianAdmin.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = $"{Constants.AppName} {Constants.AppVersion}";
    }

    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                RepositoryPathBox.Text = folder.Path;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Browse failed: {ex.Message}";
        }
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        var path = RepositoryPathBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            StatusText.Text = "Enter or browse for a repository path.";
            return;
        }

        if (!Directory.Exists(path))
        {
            StatusText.Text = $"Path not found: {path}";
            return;
        }

        var catalogs = Path.Combine(path, Constants.RepositoryDirectories.Catalogs);
        var manifests = Path.Combine(path, Constants.RepositoryDirectories.Manifests);
        var pkgsinfo = Path.Combine(path, Constants.RepositoryDirectories.PkgsInfo);
        var pkgs = Path.Combine(path, Constants.RepositoryDirectories.Pkgs);

        var report = new System.Text.StringBuilder();
        report.AppendLine($"Root:      {path}");
        report.AppendLine($"catalogs/  {(Directory.Exists(catalogs) ? "OK" : "missing")}");
        report.AppendLine($"manifests/ {(Directory.Exists(manifests) ? "OK" : "missing")}");
        report.AppendLine($"pkgsinfo/  {(Directory.Exists(pkgsinfo) ? "OK" : "missing")}");
        report.AppendLine($"pkgs/      {(Directory.Exists(pkgs) ? "OK" : "missing")}");

        if (Directory.Exists(pkgsinfo))
        {
            var count = Directory.GetFiles(pkgsinfo, "*.yaml", SearchOption.AllDirectories).Length;
            report.AppendLine($"pkginfo files: {count}");
        }
        if (Directory.Exists(manifests))
        {
            var count = Directory.GetFiles(manifests, "*.yaml", SearchOption.AllDirectories).Length;
            report.AppendLine($"manifest files: {count}");
        }
        if (Directory.Exists(catalogs))
        {
            var count = Directory.GetFiles(catalogs, "*.yaml", SearchOption.AllDirectories).Length;
            report.AppendLine($"catalog files: {count}");
        }

        StatusText.Text = report.ToString();
    }
}
