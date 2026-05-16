namespace CimianStudio.Settings;

using System.Reflection;
using CimianStudio.Shared.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

public sealed class AboutSectionProvider : ISettingsSectionProvider
{
    private readonly ISettingsService _settingsService;

    public AboutSectionProvider(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string SectionId => "about";
    public string DisplayName => "About";
    public int Order => 1000;

    public FrameworkElement BuildContent()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—";

        var panel = new StackPanel { Spacing = 6 };

        panel.Children.Add(new TextBlock
        {
            Text = $"CimianStudio  {version}",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Settings file",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(0, 4, 0, 0),
        });

        var pathText = new TextBlock
        {
            Text = _settingsService.SettingsFilePath,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(pathText);

        var openLink = new HyperlinkButton
        {
            Content = "Open settings file",
            Padding = new Thickness(0),
        };
        openLink.Click += async (_, _) =>
        {
            var path = _settingsService.SettingsFilePath;
            if (File.Exists(path))
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                await Launcher.LaunchFileAsync(file);
            }
            else
            {
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(
                    Path.GetDirectoryName(path)!);
                await Launcher.LaunchFolderAsync(folder);
            }
        };
        panel.Children.Add(openLink);

        return panel;
    }
}
