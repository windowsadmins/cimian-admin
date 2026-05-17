namespace CimianStudio.Views.Settings;

using System.Numerics;
using CimianStudio.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

public sealed partial class SettingsPage : Page
{
    private readonly IEnumerable<ISettingsSectionProvider> _providers;

    public SettingsPage(IEnumerable<ISettingsSectionProvider> providers)
    {
        _providers = providers;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SectionsPanel.Children.Clear();
        foreach (var provider in _providers.OrderBy(p => p.Order))
        {
            SectionsPanel.Children.Add(BuildCard(provider));
        }
    }

    private static Border BuildCard(ISettingsSectionProvider provider)
    {
        var header = new TextBlock
        {
            Text = provider.DisplayName,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 12),
        };

        var inner = new StackPanel();
        inner.Children.Add(header);
        inner.Children.Add(provider.BuildContent());

        var card = new Border
        {
            Padding = new Thickness(20, 16, 20, 16),
            Child = inner,
        };

        if (Application.Current.Resources.TryGetValue("CardStyle", out var style) && style is Style cardStyle)
            card.Style = cardStyle;

        if (Application.Current.Resources.TryGetValue("CardShadow", out var shadowObj) && shadowObj is Shadow shadow)
            card.Shadow = shadow;

        card.Translation = new Vector3(0, 0, 16);

        return card;
    }
}
