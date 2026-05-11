namespace CimianAdmin.ViewModels;

using System.Collections.ObjectModel;
using CimianAdmin.Core.Models.Catalogs;
using CimianAdmin.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class CatalogsViewModel : ObservableObject
{
    private readonly ICatalogService _catalogService;
    private readonly IRepositoryService _repositoryService;

    [ObservableProperty]
    public partial ObservableCollection<Catalog> Catalogs { get; set; } = [];

    [ObservableProperty]
    public partial Catalog? SelectedCatalog { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? RebuildStatus { get; set; }

    public CatalogsViewModel(ICatalogService catalogService, IRepositoryService repositoryService)
    {
        ArgumentNullException.ThrowIfNull(catalogService);
        ArgumentNullException.ThrowIfNull(repositoryService);
        _catalogService = catalogService;
        _repositoryService = repositoryService;
        _catalogService.CatalogsChanged += OnCatalogsChanged;
    }

    public async Task LoadAsync()
    {
        if (_repositoryService.CurrentRepository is null)
        {
            Catalogs = [];
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var loaded = await _catalogService.GetAllCatalogsAsync().ConfigureAwait(true);
            Catalogs = [.. loaded];
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Catalogs = [];
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RebuildAsync()
    {
        if (_repositoryService.CurrentRepository is null)
        {
            return;
        }

        IsLoading = true;
        RebuildStatus = "Running makecatalogs...";
        try
        {
            var result = await _catalogService.RebuildCatalogsAsync().ConfigureAwait(true);
            RebuildStatus = result.Success
                ? $"Rebuilt {result.RebuiltCatalogs.Count} catalog(s)."
                : $"makecatalogs failed: {result.ErrorMessage}";
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            RebuildStatus = $"makecatalogs error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnCatalogsChanged(object? sender, EventArgs e)
    {
        _ = LoadAsync();
    }
}
