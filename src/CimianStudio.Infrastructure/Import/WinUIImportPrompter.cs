namespace CimianStudio.Infrastructure.Import;

using Cimian.CLI.Cimiimport.Models;
using Cimian.CLI.Cimiimport.Services;

/// <summary>
/// <see cref="IImportPrompter"/> implementation that returns answers the
/// CimianStudio wizard already collected from the user. The wizard drives the
/// step-by-step UX itself (drag-drop, metadata edit form, scripts pivot,
/// subdir + final preview); when the user clicks Save we hand the collected
/// state to <see cref="ImportService.ImportAsync"/> with this prompter as the
/// adapter.
///
/// Status messages flow live to the optional <c>statusCallback</c> — that's
/// what the wizard's progress label uses to show "Calculating file hash...",
/// "Copying installer to repo...", etc.
///
/// This sits alongside <see cref="ConsolePrompter"/> and
/// <see cref="NoInteractivePrompter"/> upstream — the IImportPrompter
/// interface explicitly anticipates a GUI implementation; this is it.
/// </summary>
public sealed class WinUIImportPrompter : IImportPrompter
{
    private readonly bool _useTemplate;
    private readonly InstallerMetadata _editedMetadata;
    private readonly string _subdir;
    private readonly Action<string>? _statusCallback;

    public WinUIImportPrompter(
        bool useTemplate,
        InstallerMetadata editedMetadata,
        string subdir,
        Action<string>? statusCallback = null)
    {
        ArgumentNullException.ThrowIfNull(editedMetadata);
        _useTemplate = useTemplate;
        _editedMetadata = editedMetadata;
        _subdir = subdir ?? string.Empty;
        _statusCallback = statusCallback;
    }

    public Task<bool> AskUseTemplateAsync(PkgsInfo existingPkg, CancellationToken cancellationToken = default)
        => Task.FromResult(_useTemplate);

    // The wizard already presented the form and the user confirmed by clicking
    // Continue / Save — return the edited metadata as-is. ImportService doesn't
    // get to "edit" on top; we just hand back what the user typed.
    public Task<InstallerMetadata> EditMetadataAsync(InstallerMetadata seed, ImportConfiguration config, CancellationToken cancellationToken = default)
        => Task.FromResult(_editedMetadata);

    public Task<string> AskRepoSubdirAsync(string defaultPath, CancellationToken cancellationToken = default)
        => Task.FromResult(_subdir);

    // Save click in Step 4 IS the confirmation. Returning false here would
    // leave the user with a half-completed import in a confusing state, so
    // we just affirm — Cancel / Back is how the user backs out before this point.
    public Task<bool> ConfirmImportAsync(PkgsInfo finalPkginfo, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public void ReportInfo(string message) => _statusCallback?.Invoke(message);

    public void ReportWarning(string message) => _statusCallback?.Invoke($"WARNING: {message}");

    public void ReportError(string message) => _statusCallback?.Invoke($"ERROR: {message}");
}
