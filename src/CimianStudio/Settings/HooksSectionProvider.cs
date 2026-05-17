namespace CimianStudio.Settings;

using CimianStudio.Shared.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

public sealed class HooksSectionProvider : ISettingsSectionProvider
{
    public const string Id = "hooks";

    private readonly ISettingsService _settingsService;

    public HooksSectionProvider(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string SectionId { get; } = Id;
    public string DisplayName => "Git Hooks";
    public int Order => 10;

    public FrameworkElement BuildContent()
    {
        var settings = _settingsService.GetSection<HooksSettings>(Id);

        var panel = new StackPanel { Spacing = 8 };

        panel.Children.Add(new TextBlock
        {
            Text = "Hooks directory override",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Leave blank to auto-discover (core.hooksPath → .githooks/ → .git/hooks/).",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        var pathBox = new TextBox
        {
            PlaceholderText = "e.g. C:\\repo\\.githooks  or  .githooks",
            Text = settings.OverridePath ?? string.Empty,
        };
        panel.Children.Add(pathBox);

        var applyBtn = new Button { Content = "Apply", Margin = new Thickness(0, 4, 0, 0) };
        applyBtn.Click += async (_, _) =>
        {
            var updated = new HooksSettings
            {
                OverridePath = string.IsNullOrWhiteSpace(pathBox.Text)
                    ? null
                    : pathBox.Text.Trim(),
            };
            await _settingsService.SetSectionAsync<HooksSettings>(Id, updated).ConfigureAwait(false);
        };
        panel.Children.Add(applyBtn);

        return panel;
    }
}
