namespace CimianStudio.Infrastructure.Services;

using System.Collections.Concurrent;
using CimianStudio.Core.Models.Search;
using CimianStudio.Core.Services;
using CimianStudio.Shared;

/// <summary>
/// In-memory full-text index over pkgsinfo + manifests.
/// One <see cref="FileSystemWatcher"/> per watched subdirectory; changes debounced ~200ms.
/// Queries scan <see cref="IndexEntry.LowerText"/> with <c>IndexOf(StringComparison.Ordinal)</c>
/// after lowercasing the query — avoids per-char culture work on every line.
/// </summary>
public sealed class SearchService : ISearchService, IDisposable
{
    private const int DebounceMs = 200;

    private readonly ConcurrentDictionary<string, IndexEntry> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, CancellationTokenSource> _pendingReindex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _watcherLock = new();
    // Serializes Start/Stop so overlapping callers can't leave watchers attached
    // to one root while the index is being built for another. An InitialScan
    // in flight is cancelled via _lifetimeCts before the next Start proceeds.
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private CancellationTokenSource? _lifetimeCts;
    private bool _isReady;

    public bool IsReady => _isReady;

    public event EventHandler<SearchIndexProgress>? ProgressChanged;

    public async Task StartAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _lifetimeCts = cts;
            var token = cts.Token;
            _isReady = false;

            await Task.Run(() => InitialScan(repositoryRoot, token), token).ConfigureAwait(false);

            AttachWatcher(Path.Combine(repositoryRoot, Constants.RepositoryDirectories.PkgsInfo), SearchHitKind.Package);
            AttachWatcher(Path.Combine(repositoryRoot, Constants.RepositoryDirectories.Manifests), SearchHitKind.Manifest);

            _isReady = true;
            ProgressChanged?.Invoke(this, new SearchIndexProgress(_index.Count, _index.Count, IsComplete: true));
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopInternalAsync()
    {
        // Cancel any in-flight InitialScan or debounced re-index before we tear down state.
        if (_lifetimeCts is { } cts)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
            _lifetimeCts = null;
        }

        List<CancellationTokenSource> pending;
        lock (_watcherLock)
        {
            foreach (var w in _watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();

            pending = [.. _pendingReindex.Values];
            _pendingReindex.Clear();
        }
        foreach (var pendCts in pending)
        {
            await pendCts.CancelAsync().ConfigureAwait(false);
            pendCts.Dispose();
        }
        _index.Clear();
        _isReady = false;
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>([]);
        }

        var needle = query.Trim().ToLowerInvariant();
        // Run the scan on the thread pool — IndexOf across a few MB of text is fast
        // but the UI thread shouldn't pay for it on huge repos.
        return Task.Run<IReadOnlyList<SearchHit>>(() =>
        {
            var hits = new List<SearchHit>(maxResults);
            foreach (var entry in _index.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var idx = entry.LowerText.IndexOf(needle, StringComparison.Ordinal);
                if (idx < 0) continue;

                var lineNumber = FindLineNumber(entry.LowerText, idx);
                var snippet = BuildSnippet(entry.Lines, lineNumber);
                hits.Add(new SearchHit(entry.AbsolutePath, entry.DisplayName, entry.Kind, lineNumber, snippet));
                if (hits.Count >= maxResults) break;
            }
            return hits;
        }, cancellationToken);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        // StopAsync already disposes _lifetimeCts and clears it; this guard satisfies
        // the analyzer in case a future Stop path leaves the field set.
        _lifetimeCts?.Dispose();
        _lifecycleGate.Dispose();
    }

    private void InitialScan(string root, CancellationToken ct)
    {
        var pkgs = SafeEnumerate(Path.Combine(root, Constants.RepositoryDirectories.PkgsInfo));
        var manifests = SafeEnumerate(Path.Combine(root, Constants.RepositoryDirectories.Manifests));
        var all = pkgs.Select(p => (p, SearchHitKind.Package))
                      .Concat(manifests.Select(p => (p, SearchHitKind.Manifest)))
                      .ToList();

        var total = all.Count;
        var done = 0;
        foreach (var (path, kind) in all)
        {
            ct.ThrowIfCancellationRequested();
            IndexFile(path, kind);
            done++;
            if (done % 25 == 0 || done == total)
            {
                ProgressChanged?.Invoke(this, new SearchIndexProgress(done, total, IsComplete: done == total));
            }
        }
    }

    private static List<string> SafeEnumerate(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        try
        {
            return [.. Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(IsYaml)];
        }
        catch
        {
            return [];
        }
    }

    private static bool IsYaml(string path) =>
        path.EndsWith(Constants.FileExtensions.Yaml, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(Constants.FileExtensions.Yml, StringComparison.OrdinalIgnoreCase);

    private void IndexFile(string absolutePath, SearchHitKind kind)
    {
        try
        {
            var lines = File.ReadAllLines(absolutePath);
            var lower = string.Join('\n', lines).ToLowerInvariant();
            var display = Path.GetFileNameWithoutExtension(absolutePath);
            _index[absolutePath] = new IndexEntry(absolutePath, display, kind, lines, lower);
        }
        catch
        {
            _index.TryRemove(absolutePath, out IndexEntry? _);
        }
    }

    private void AttachWatcher(string directory, SearchHitKind kind)
    {
        if (!Directory.Exists(directory)) return;

        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Filters.Add("*.yaml");
        watcher.Filters.Add("*.yml");

        watcher.Created += (_, e) => Schedule(e.FullPath, kind);
        watcher.Changed += (_, e) => Schedule(e.FullPath, kind);
        watcher.Renamed += (_, e) =>
        {
            _index.TryRemove(e.OldFullPath, out IndexEntry? _);
            Schedule(e.FullPath, kind);
        };
        watcher.Deleted += (_, e) => _index.TryRemove(e.FullPath, out IndexEntry? _);
        watcher.EnableRaisingEvents = true;

        lock (_watcherLock)
        {
            _watchers.Add(watcher);
        }
    }

    private void Schedule(string path, SearchHitKind kind)
    {
        if (!IsYaml(path)) return;

        CancellationTokenSource cts;
        lock (_watcherLock)
        {
            if (_pendingReindex.TryGetValue(path, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }
            cts = new CancellationTokenSource();
            _pendingReindex[path] = cts;
        }

        _ = Task.Delay(DebounceMs, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            IndexFile(path, kind);
            lock (_watcherLock)
            {
                if (_pendingReindex.TryGetValue(path, out var current) && current == cts)
                {
                    _pendingReindex.Remove(path);
                    cts.Dispose();
                }
            }
            // Tell listeners the index just changed so the indexing pill can briefly
            // pulse on edits, matching the ProgressChanged contract.
            ProgressChanged?.Invoke(this, new SearchIndexProgress(_index.Count, _index.Count, IsComplete: true));
        }, TaskScheduler.Default);
    }

    private static int FindLineNumber(string lowerText, int charIndex)
    {
        var line = 1;
        for (int i = 0; i < charIndex && i < lowerText.Length; i++)
        {
            if (lowerText[i] == '\n') line++;
        }
        return line;
    }

    private static string BuildSnippet(string[] lines, int lineNumber)
    {
        var idx = lineNumber - 1;
        if (idx < 0 || idx >= lines.Length) return string.Empty;
        var start = Math.Max(0, idx - 1);
        var end = Math.Min(lines.Length - 1, idx + 1);
        var sb = new System.Text.StringBuilder();
        for (int i = start; i <= end; i++)
        {
            if (i > start) sb.Append('\n');
            sb.Append(lines[i].TrimEnd());
        }
        return sb.ToString();
    }

    private sealed record IndexEntry(
        string AbsolutePath,
        string DisplayName,
        SearchHitKind Kind,
        string[] Lines,
        string LowerText);
}
