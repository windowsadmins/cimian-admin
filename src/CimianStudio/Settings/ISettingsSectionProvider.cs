namespace CimianStudio.Settings;

using Microsoft.UI.Xaml;

/// <summary>
/// Contributes a settings card to <c>SettingsPage</c>.
/// Register as <c>ISettingsSectionProvider</c> singleton in <c>App.xaml.cs</c>.
/// <see cref="BuildContent"/> returns the card's inner content; <c>SettingsPage</c>
/// wraps it in a styled card border with a section header.
/// </summary>
public interface ISettingsSectionProvider
{
    /// <summary>Stable identifier, e.g. <c>"about"</c>, <c>"build"</c>, <c>"pipelines"</c>.</summary>
    string SectionId { get; }

    /// <summary>Header text shown above the card.</summary>
    string DisplayName { get; }

    /// <summary>Sort key; lower values appear first. Use multiples of 10 for easy insertion.</summary>
    int Order { get; }

    /// <summary>
    /// Returns the UI content for this section. Called once per <c>SettingsPage</c> load.
    /// May access <c>Application.Current.Resources</c> for theme brushes and styles.
    /// </summary>
    FrameworkElement BuildContent();
}
