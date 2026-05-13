namespace CimianAdmin.Views.Import;

using System.Collections.ObjectModel;
using CimianAdmin.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Orchestrates the cimiimport-native Import wizard. Holds the queue of files
/// the user dropped or picked; M3 extends this with per-file wizard state
/// (current step, metadata buffer, prompter task-completions) and drives the
/// underlying <c>Cimian.CLI.Cimiimport.Services.ImportService</c> via a
/// WinUI-backed <c>IImportPrompter</c>.
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly IRepositoryService _repositoryService;

    /// <summary>One entry in the import queue.</summary>
    public sealed class QueueItem(string filePath)
    {
        public string FilePath { get; } = filePath;
        public string FileName => System.IO.Path.GetFileName(FilePath);
        /// <summary>Workflow state — currently just "pending"; M6 adds in-progress/done/error.</summary>
        public string Status { get; set; } = "pending";
    }

    /// <summary>The queue, surfaced to the page for ListView binding when M6 lands.</summary>
    public ObservableCollection<QueueItem> Queue { get; } = [];

    public ImportViewModel(IRepositoryService repositoryService)
    {
        ArgumentNullException.ThrowIfNull(repositoryService);
        _repositoryService = repositoryService;
    }

    /// <summary>Append files to the queue. Duplicates (by full path) are ignored.</summary>
    public void EnqueueFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (Queue.Any(q => string.Equals(q.FilePath, p, StringComparison.OrdinalIgnoreCase))) continue;
            Queue.Add(new QueueItem(p));
        }
    }
}
