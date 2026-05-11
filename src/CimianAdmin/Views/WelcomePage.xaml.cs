namespace CimianAdmin.Views;

using CimianAdmin.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class WelcomePage : Page
{
    public MainViewModel ViewModel { get; }

    public WelcomePage(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
    }

    public Visibility HasRecentsVisibility =>
        ViewModel.RecentRepositories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        if (App.MainWindowInstance is { } window)
        {
            await window.PromptAndOpenRepositoryAsync().ConfigureAwait(true);
        }
    }

    private async void OnRecentClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string path)
        {
            await ViewModel.OpenRepositoryAsync(path).ConfigureAwait(true);
        }
    }
}
