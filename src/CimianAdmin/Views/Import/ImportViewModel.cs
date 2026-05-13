namespace CimianAdmin.Views.Import;

using System.Collections.ObjectModel;
using CimianAdmin.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Orchestrates the cimiimport-native Import wizard. Holds the queue of files
/// the user dropped or picked; the wizard drives per-file state for the
/// in-progress import, while the queue here tracks the batch.
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly IRepositoryService _repositoryService;

    /// <summary>
    /// State of a single queue row. Drives the icon + text in the batch list.
    /// </summary>
    public enum QueueItemStatus
    {
        Pending,
        InProgress,
        Done,
        Error,
    }

    /// <summary>
    /// One entry in the import queue. Notifies on Status / StatusText so the
    /// XAML can re-render the row without us rebuilding the whole list.
    /// </summary>
    public sealed partial class QueueItem : ObservableObject
    {
        public string FilePath { get; }
        public string FileName => System.IO.Path.GetFileName(FilePath);

        [ObservableProperty]
        public partial QueueItemStatus Status { get; set; }

        /// <summary>Human-readable status detail (e.g. error message, "Done", target path).</summary>
        [ObservableProperty]
        public partial string StatusText { get; set; }

        public QueueItem(string filePath)
        {
            FilePath = filePath;
            Status = QueueItemStatus.Pending;
            StatusText = "Queued";
        }
    }

    /// <summary>The queue, surfaced to the page for ListView binding.</summary>
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
