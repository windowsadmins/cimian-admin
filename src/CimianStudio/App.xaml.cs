namespace CimianStudio;

using CimianStudio.Core.Services;
using CimianStudio.Infrastructure.Services;
using CimianStudio.Infrastructure.Settings;
using CimianStudio.Shared.Settings;
using CimianStudio.ViewModels;
using CimianStudio.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

public partial class App : Application
{
    public static IHost Host { get; private set; } = null!;

    public static MainWindow? MainWindowInstance { get; private set; }

    /// <summary>
    /// One-shot package selection consumed by the next-loaded <c>PackagesPage</c>.
    /// Used by cross-page navigation (e.g. catalog row → open in package editor).
    /// </summary>
    public static CimianStudio.Core.Models.Packages.Package? PendingPackageSelection { get; set; }

    /// <summary>One-shot manifest selection consumed by the next-loaded <c>ManifestsPage</c>.</summary>
    public static CimianStudio.Core.Models.Manifests.Manifest? PendingManifestSelection { get; set; }

    public static T Resolve<T>() where T : notnull => Host.Services.GetRequiredService<T>();

    public App()
    {
        InitializeComponent();
        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<IRepositoryService, RepositoryService>();
                services.AddSingleton<IPackageService, PackageService>();
                services.AddSingleton<IManifestService, ManifestService>();
                services.AddSingleton<ICatalogService, CatalogService>();
                services.AddSingleton<IGitService, GitService>();
                services.AddSingleton<ISearchService, SearchService>();
                services.AddSingleton<ISessionState, EditorSessionState>();

                services.AddSingleton<MainViewModel>();
                services.AddTransient<PackagesViewModel>();
                services.AddTransient<ManifestsViewModel>();
                services.AddTransient<CatalogsViewModel>();
                services.AddTransient<Views.Import.ImportViewModel>();

                services.AddTransient<MainWindow>();

                services.AddTransient<WelcomePage>();
                services.AddTransient<RepositoryPage>();
                services.AddTransient<PackagesPage>();
                services.AddTransient<ManifestsPage>();
                services.AddTransient<CatalogsPage>();
                // GitPage + ImportPage are singletons so cross-tab handoffs
                // (Import → Git, Packages drop → Import) operate on the *visible*
                // page instance instead of a fresh transient that isn't attached
                // to ContentFrame. As a bonus, in-progress wizard state survives
                // a brief tab switch.
                services.AddSingleton<GitPage>();
                services.AddSingleton<Views.Import.ImportPage>();

                services.AddTransient<PackageEditorWindow>();
                services.AddTransient<ManifestEditorWindow>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = Resolve<MainWindow>();
        MainWindowInstance = window;
        window.Activate();
        await Resolve<MainViewModel>().InitializeAsync(window).ConfigureAwait(true);
    }
}
