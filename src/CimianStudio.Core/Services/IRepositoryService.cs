namespace CimianStudio.Core.Services;

using CimianStudio.Core.Models.Repository;

/// <summary>
/// Service interface for repository operations.
/// </summary>
public interface IRepositoryService
{
    /// <summary>
    /// Opens an existing Cimian repository.
    /// </summary>
    /// <param name="path">Path to the repository root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The opened repository.</returns>
    Task<CimianRepository> OpenRepositoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new Cimian repository.
    /// </summary>
    /// <param name="path">Path where the repository will be created.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created repository.</returns>
    Task<CimianRepository> CreateRepositoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the repository structure.
    /// </summary>
    /// <param name="repository">Repository to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with any errors.</returns>
    Task<RepositoryValidationResult> ValidateRepositoryAsync(CimianRepository repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes repository statistics.
    /// </summary>
    /// <param name="repository">Repository to refresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshStatisticsAsync(CimianRepository repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the currently open repository.
    /// </summary>
    CimianRepository? CurrentRepository { get; }

    /// <summary>
    /// Event raised when the current repository changes.
    /// </summary>
    event EventHandler<CimianRepository?>? RepositoryChanged;
}

/// <summary>
/// Result of repository validation.
/// </summary>
public sealed class RepositoryValidationResult
{
    /// <summary>
    /// Gets whether the repository is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets the validation warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
