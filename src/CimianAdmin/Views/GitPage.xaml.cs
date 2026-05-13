namespace CimianAdmin.Views;

using System.Globalization;
using CimianAdmin.Core.Models.Git;
using CimianAdmin.Core.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

/// <summary>
/// Dedicated Git workspace. Left pane: file checklist + commit composer.
/// Right pane: unified-diff viewer for the selected file. Splitter between is
/// draggable. Header offers a branch picker that does an in-process checkout
/// (blocked if the working tree is dirty).
/// </summary>
public sealed partial class GitPage : Page
{
    private readonly IRepositoryService _repositoryService;
    private readonly IGitService _gitService;
    private readonly IPackageService _packageService;
    private readonly IManifestService _manifestService;

    private GitRepositoryInfo? _info;
    private List<ChangeRow> _rows = [];
    private bool _suppressBranchChange;
    private ChangeRow? _selectedRow;
    // Re-ticks the "Last pulled Xm ago" caption from .git/FETCH_HEAD mtime. Once
    // a minute is plenty — relative time deltas under a minute show as "just now".
    private Microsoft.UI.Xaml.DispatcherTimer? _lastFetchTimer;

    private static readonly Color AddColor = Color.FromArgb(0xFF, 0x4E, 0xC9, 0x70);
    private static readonly Color RemoveColor = Color.FromArgb(0xFF, 0xE7, 0x6F, 0x6F);
    private static readonly Color HunkColor = Color.FromArgb(0xFF, 0x77, 0x9D, 0xFF);
    private static readonly Color MutedColor = Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A);

    public GitPage(
        IRepositoryService repositoryService,
        IGitService gitService,
        IPackageService packageService,
        IManifestService manifestService)
    {
        ArgumentNullException.ThrowIfNull(repositoryService);
        ArgumentNullException.ThrowIfNull(gitService);
        ArgumentNullException.ThrowIfNull(packageService);
        ArgumentNullException.ThrowIfNull(manifestService);
        _repositoryService = repositoryService;
        _gitService = gitService;
        _packageService = packageService;
        _manifestService = manifestService;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lastFetchTimer ??= new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _lastFetchTimer.Tick -= OnLastFetchTimerTick;
        _lastFetchTimer.Tick += OnLastFetchTimerTick;
        _lastFetchTimer.Start();
        await RefreshAsync().ConfigureAwait(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_lastFetchTimer is not null)
        {
            _lastFetchTimer.Stop();
            _lastFetchTimer.Tick -= OnLastFetchTimerTick;
        }
    }

    private void OnLastFetchTimerTick(object? sender, object e) => UpdateLastFetchCaption();

    /// <summary>
    /// Reads <c>.git/FETCH_HEAD</c> mtime and renders the relative-time caption
    /// (e.g. "Last fetched 3m ago"). FETCH_HEAD is touched by every <c>git fetch</c>
    /// or <c>git pull</c>, including ones the user runs outside the app — so this
    /// stays accurate without us tracking state in memory.
    /// </summary>
    private void UpdateLastFetchCaption()
    {
        if (_info is null || string.IsNullOrEmpty(_info.GitRoot))
        {
            LastFetchText.Text = string.Empty;
            return;
        }

        var fetchHead = Path.Combine(_info.GitRoot, ".git", "FETCH_HEAD");
        if (!File.Exists(fetchHead))
        {
            LastFetchText.Text = "never fetched";
            return;
        }

        var when = File.GetLastWriteTime(fetchHead);
        LastFetchText.Text = $"Last fetched {FormatRelativeTime(DateTime.Now - when)}";
    }

    /// <summary>
    /// Compact git-style relative duration: seconds → <c>just now</c>, otherwise
    /// the largest non-zero unit only (e.g. <c>3m ago</c>, <c>2h ago</c>, <c>5d ago</c>).
    /// Caps weeks/months/years at days so the caption never reaches surprising
    /// units in a long-lived repo.
    /// </summary>
    private static string FormatRelativeTime(TimeSpan delta)
    {
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
        return $"{(int)delta.TotalDays}d ago";
    }

    /// <summary>
    /// External entry point: refreshes status, selects the rows whose paths match
    /// <paramref name="absoluteFilePaths"/>, and pre-fills the commit composer.
    /// Used by the Import wizard / batch queue to hand off a freshly-imported set
    /// of files to the Git tab with a sensible default commit message — saves the
    /// user from manually ticking each one.
    /// </summary>
    public async Task PrepareCommitAsync(IReadOnlyList<string> absoluteFilePaths, string subject, string? body = null)
    {
        ArgumentNullException.ThrowIfNull(absoluteFilePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        await RefreshAsync().ConfigureAwait(true);
        if (_info is null) return;

        // Translate each absolute path to the git-root-relative form that
        // GitStatusEntry.RelativePath carries; case-insensitive match because
        // Windows paths often differ in casing between the picker and the
        // working tree.
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var abs in absoluteFilePaths)
        {
            if (string.IsNullOrWhiteSpace(abs)) continue;
            var rel = Path.GetRelativePath(_info.GitRoot, abs).Replace('\\', '/');
            wanted.Add(rel);
        }

        foreach (var row in _rows)
        {
            row.IsSelected = wanted.Contains(row.Entry.RelativePath);
        }

        SubjectBox.Text = subject;
        BodyBox.Text = body ?? string.Empty;
        RenderChanges();
        UpdateCommitEnabled();
    }

    public async Task RefreshAsync()
    {
        var repo = _repositoryService.CurrentRepository;
        if (repo is null)
        {
            ShowNoGit("No repository is open.");
            return;
        }

        try
        {
            _info = await _gitService.DiscoverAsync(repo.RootPath).ConfigureAwait(true);
        }
        catch
        {
            _info = null;
        }

        if (_info is null)
        {
            ShowNoGit("No git repository detected for this Cimian deployment.");
            return;
        }

        AheadBehindText.Text = _info.HasUpstream
            ? string.Create(CultureInfo.InvariantCulture, $"↑{_info.AheadCount} ↓{_info.BehindCount}")
            : "no upstream";
        AheadBehindChip.Visibility = Visibility.Visible;

        var rootName = Path.GetFileName(_info.GitRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        ScopeLine.Text = string.IsNullOrEmpty(_info.RelativeRepoPath)
            ? $"Scoped to {rootName}"
            : $"Scoped to {_info.RelativeRepoPath} (in {rootName})";

        await PopulateBranchesAsync().ConfigureAwait(true);
        await PopulateIdentityAsync().ConfigureAwait(true);

        IReadOnlyList<GitStatusEntry> entries;
        try
        {
            entries = await _gitService.GetStatusAsync(_info).ConfigureAwait(true);
        }
        catch
        {
            entries = [];
        }

        _rows = [.. entries.Select(static e => new ChangeRow(e))];
        _selectedRow = null;
        RenderChanges();
        ClearDiff();
        UpdateLastFetchCaption();
    }

    private async Task PopulateIdentityAsync()
    {
        if (_info is null) return;
        GitIdentity identity;
        try
        {
            identity = await _gitService.GetIdentityAsync(_info).ConfigureAwait(true);
        }
        catch
        {
            identity = new GitIdentity(string.Empty, string.Empty);
        }

        if (string.IsNullOrEmpty(identity.Name) && string.IsNullOrEmpty(identity.Email))
        {
            IdentityText.Text = "(no identity set)";
        }
        else if (string.IsNullOrEmpty(identity.Email))
        {
            IdentityText.Text = identity.Name;
        }
        else
        {
            IdentityText.Text = $"{identity.Name} <{identity.Email}>";
        }
    }

    private async void OnEditIdentityClicked(object sender, RoutedEventArgs e)
    {
        if (_info is null) return;
        GitIdentity current;
        try
        {
            current = await _gitService.GetIdentityAsync(_info).ConfigureAwait(true);
        }
        catch
        {
            current = new GitIdentity(string.Empty, string.Empty);
        }

        var nameBox = new TextBox { PlaceholderText = "Full name", Text = current.Name, MinWidth = 320 };
        var emailBox = new TextBox { PlaceholderText = "you@example.com", Text = current.Email, MinWidth = 320 };
        var scopePicker = new ComboBox { MinWidth = 320 };
        scopePicker.ItemsSource = new List<string> { "This repository only", "Global (~/.gitconfig)" };
        scopePicker.SelectedIndex = 0;

        var testButton = new Button { Content = "Test authentication" };
        var testRing = new ProgressRing { Width = 16, Height = 16, IsActive = false, VerticalAlignment = VerticalAlignment.Center };
        var testStatus = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var testRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        testRow.Children.Add(testButton);
        testRow.Children.Add(testRing);
        testRow.Children.Add(testStatus);

        testButton.Click += async (_, _) =>
        {
            if (_info is null) return;
            testButton.IsEnabled = false;
            testRing.IsActive = true;
            testStatus.Text = "Running git ls-remote…";
            testStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            try
            {
                var result = await _gitService.TestAuthAsync(_info).ConfigureAwait(true);
                testStatus.Text = result.Success ? "Auth OK — remote responded." : Truncate(result.Output, 240);
                testStatus.Foreground = result.Success
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }
            catch (Exception ex)
            {
                testStatus.Text = ex.Message;
                testStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }
            finally
            {
                testRing.IsActive = false;
                testButton.IsEnabled = true;
            }
        };

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = "Name", Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        body.Children.Add(nameBox);
        body.Children.Add(new TextBlock { Text = "Email", Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        body.Children.Add(emailBox);
        body.Children.Add(new TextBlock { Text = "Scope", Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        body.Children.Add(scopePicker);
        body.Children.Add(new Border
        {
            Margin = new Thickness(0, 4, 0, 0),
            Height = 1,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        });
        body.Children.Add(testRow);

        var dialog = new ContentDialog
        {
            Title = "Git identity",
            Content = body,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            ShowError("Couldn't open identity editor", ex.Message);
            return;
        }
        if (result != ContentDialogResult.Primary) return;

        var name = nameBox.Text?.Trim() ?? string.Empty;
        var email = emailBox.Text?.Trim() ?? string.Empty;
        if (name.Length == 0 || email.Length == 0)
        {
            ShowError("Identity not saved", "Name and email are both required.");
            return;
        }

        var scope = scopePicker.SelectedIndex == 1 ? GitConfigScope.Global : GitConfigScope.Local;
        try
        {
            await _gitService.SetIdentityAsync(_info, name, email, scope).ConfigureAwait(true);
            await PopulateIdentityAsync().ConfigureAwait(true);
            ShowSuccess($"Identity set ({(scope == GitConfigScope.Global ? "global" : "local")}).");
        }
        catch (Exception ex)
        {
            ShowError("Failed to set identity", ex.Message);
        }
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var trimmed = value.Replace("\r", string.Empty, StringComparison.Ordinal).Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }

    private Progress<string> ShowProgress(string title)
    {
        ProgressTitle.Text = title;
        ProgressText.Text = string.Empty;
        ProgressOverlay.Visibility = Visibility.Visible;
        ProgressRing.IsActive = true;
        ResultBar.IsOpen = false;

        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        return new Progress<string>(line =>
        {
            // Append on the UI thread; Progress<T> already marshals to the captured
            // context, but git's stdout pipe runs on a thread-pool worker.
            if (dispatcher is null) AppendProgress(line);
            else dispatcher.TryEnqueue(() => AppendProgress(line));
        });
    }

    private void AppendProgress(string line)
    {
        if (ProgressText.Text.Length > 0) ProgressText.Text += "\n";
        ProgressText.Text += line;
        ProgressScroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);
    }

    private void HideProgress()
    {
        ProgressRing.IsActive = false;
        ProgressOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task PopulateBranchesAsync()
    {
        if (_info is null) return;
        IReadOnlyList<GitBranch> branches;
        try
        {
            branches = await _gitService.GetBranchesAsync(_info).ConfigureAwait(true);
        }
        catch
        {
            branches = [];
        }

        _suppressBranchChange = true;
        try
        {
            BranchPicker.ItemsSource = branches.Select(b => b.Name).ToList();
            var current = branches.FirstOrDefault(b => b.IsCurrent)?.Name ?? _info.Branch;
            if (!string.IsNullOrEmpty(current))
            {
                BranchPicker.SelectedItem = current;
            }
        }
        finally
        {
            _suppressBranchChange = false;
        }
    }

    private void ShowNoGit(string message)
    {
        _info = null;
        _rows = [];
        _selectedRow = null;
        AheadBehindText.Text = string.Empty;
        AheadBehindChip.Visibility = Visibility.Collapsed;
        ScopeLine.Text = string.Empty;
        BranchPicker.ItemsSource = null;
        BranchPicker.IsEnabled = false;
        ChangesHeader.Text = "Changes";
        ChangesList.ItemsSource = null;
        ChangesList.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        NoGitState.Text = message;
        NoGitState.Visibility = Visibility.Visible;
        SubjectBox.IsEnabled = false;
        BodyBox.IsEnabled = false;
        CommitButton.IsEnabled = false;
        CommitPushButton.IsEnabled = false;
        SelectAllButton.IsEnabled = false;
        SelectNoneButton.IsEnabled = false;
        LastFetchText.Text = string.Empty;
        ClearDiff();
    }

    private void RenderChanges()
    {
        NoGitState.Visibility = Visibility.Collapsed;
        BranchPicker.IsEnabled = true;
        ChangesHeader.Text = string.Create(CultureInfo.InvariantCulture, $"Changes ({_rows.Count})");
        SubjectBox.IsEnabled = true;
        BodyBox.IsEnabled = true;
        SelectAllButton.IsEnabled = _rows.Count > 0;
        SelectNoneButton.IsEnabled = _rows.Count > 0;

        if (_rows.Count == 0)
        {
            ChangesList.ItemsSource = null;
            ChangesList.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            UpdateCommitEnabled();
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        ChangesList.Visibility = Visibility.Visible;
        ChangesList.ItemsSource = _rows.Select(BuildRowControl).ToList();
        UpdateCommitEnabled();
    }

    private Grid BuildRowControl(ChangeRow row)
    {
        // Three-column row: [checkbox | status letter | relative path]. The CheckBox
        // owns its own hit area only; clicks elsewhere in the row bubble to the
        // ListView and trigger SelectionChanged → diff load.
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnSpacing = 8,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cb = new CheckBox
        {
            IsChecked = row.IsSelected,
            MinWidth = 0,
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = row,
        };
        cb.Checked += (_, _) => { row.IsSelected = true; UpdateCommitEnabled(); };
        cb.Unchecked += (_, _) => { row.IsSelected = false; UpdateCommitEnabled(); };
        Grid.SetColumn(cb, 0);
        grid.Children.Add(cb);

        var letter = new TextBlock
        {
            Text = StatusLetter(row.Entry.Status),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Width = 28,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(letter, 1);
        grid.Children.Add(letter);

        var path = new TextBlock
        {
            Text = row.Entry.RelativePath,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(path, 2);
        grid.Children.Add(path);

        return grid;
    }

    private static string StatusLetter(GitFileStatus status) => status switch
    {
        GitFileStatus.Modified => "M ",
        GitFileStatus.Added => "A ",
        GitFileStatus.Deleted => "D ",
        GitFileStatus.Renamed => "R ",
        GitFileStatus.Untracked => "??",
        GitFileStatus.Conflicted => "U ",
        _ => "  ",
    };

    private void OnSubjectChanged(object sender, TextChangedEventArgs e) => UpdateCommitEnabled();

    private void UpdateCommitEnabled()
    {
        var hasSelection = _rows.Any(r => r.IsSelected);
        var hasSubject = !string.IsNullOrWhiteSpace(SubjectBox.Text);
        var enabled = _info is not null && hasSelection && hasSubject;
        CommitButton.IsEnabled = enabled;
        CommitPushButton.IsEnabled = enabled && (_info?.HasUpstream ?? false);
        AmendCheckBox.IsEnabled = _info is not null;
        SkipHooksCheckBox.IsEnabled = _info is not null;
    }

    private void OnSelectAllClicked(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = true;
        RenderChanges();
    }

    private void OnSelectNoneClicked(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = false;
        RenderChanges();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(true);
        _historyLoaded = false;
        if (BodyPivot.SelectedIndex == 1)
        {
            await LoadHistoryAsync().ConfigureAwait(true);
        }
    }

    private bool _historyLoaded;

    private async void OnPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BodyPivot.SelectedIndex == 1 && !_historyLoaded)
        {
            await LoadHistoryAsync().ConfigureAwait(true);
        }
    }

    private async Task LoadHistoryAsync()
    {
        if (_info is null)
        {
            HistoryList.ItemsSource = null;
            HistoryEmpty.Visibility = Visibility.Visible;
            return;
        }

        HistoryLoading.IsActive = true;
        HistoryEmpty.Visibility = Visibility.Collapsed;
        try
        {
            var commits = await _gitService.GetHistoryAsync(_info, 200).ConfigureAwait(true);
            var rows = commits.Select(c => new HistoryRow(c)).ToList();
            HistoryList.ItemsSource = rows;
            HistoryEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _historyLoaded = true;
        }
        finally
        {
            HistoryLoading.IsActive = false;
        }
    }

    private async void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView lv || lv.SelectedItem is not HistoryRow row || _info is null)
        {
            HistoryDiffText.Inlines.Clear();
            HistoryDiffPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        HistoryDiffPlaceholder.Visibility = Visibility.Collapsed;
        HistoryDiffLoading.IsActive = true;
        HistoryDiffText.Inlines.Clear();
        try
        {
            var diff = await _gitService.GetCommitDiffAsync(_info, row.Sha).ConfigureAwait(true);
            if (string.IsNullOrEmpty(diff))
            {
                HistoryDiffText.Inlines.Add(new Run
                {
                    Text = "(no diff — root commit or merge)",
                    Foreground = new SolidColorBrush(MutedColor),
                });
            }
            else
            {
                RenderColorizedDiff(diff, HistoryDiffText);
            }
        }
        catch (Exception ex)
        {
            HistoryDiffText.Inlines.Add(new Run
            {
                Text = $"(failed to compute diff: {ex.Message})",
                Foreground = new SolidColorBrush(MutedColor),
            });
        }
        finally
        {
            HistoryDiffLoading.IsActive = false;
        }
    }

    private async void OnFetchClicked(object sender, RoutedEventArgs e)
    {
        if (_info is null) return;
        var progress = ShowProgress("Fetching from remote…");
        try
        {
            var result = await _gitService.FetchAsync(_info, progress).ConfigureAwait(true);
            if (result.Success) ShowSuccess("Fetch complete.");
            else ShowError("Fetch failed", result.Output);
        }
        catch (Exception ex)
        {
            ShowError("Fetch failed", ex.Message);
        }
        finally
        {
            HideProgress();
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private async void OnPullClicked(object sender, RoutedEventArgs e)
    {
        if (_info is null) return;
        var progress = ShowProgress("Pulling (rebase + autostash)…");
        try
        {
            var result = await _gitService.PullAsync(_info, progress).ConfigureAwait(true);
            if (result.Success) ShowSuccess("Pull complete.");
            else ShowError("Pull failed", result.Output);
        }
        catch (Exception ex)
        {
            ShowError("Pull failed", ex.Message);
        }
        finally
        {
            HideProgress();
            _historyLoaded = false;
            await RefreshAsync().ConfigureAwait(true);
            if (BodyPivot.SelectedIndex == 1)
            {
                await LoadHistoryAsync().ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// Right-click → "Copy as .patch" on a history row. Renders the commit via
    /// LibGit2Sharp into an mbox-style patch (see <c>GitService.FormatPatchCore</c>)
    /// and stuffs it onto the clipboard so the user can paste into a PR review or
    /// pipe into <c>git am</c>.
    /// </summary>
    private async void OnCopyPatchClicked(object sender, RoutedEventArgs e)
    {
        if (_info is null || sender is not MenuFlyoutItem item || item.DataContext is not HistoryRow row)
        {
            return;
        }

        try
        {
            var patch = await _gitService.FormatPatchAsync(_info, row.Sha).ConfigureAwait(true);
            if (string.IsNullOrEmpty(patch))
            {
                ShowError("Copy as .patch failed", "git couldn't render that commit as a patch.");
                return;
            }

            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            data.SetText(patch);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            ShowSuccess($"Copied {row.Sha} as .patch to clipboard.");
        }
        catch (Exception ex)
        {
            ShowError("Copy as .patch failed", ex.Message);
        }
    }

    /// <summary>
    /// Right-click → "Copy commit SHA". Uses the abbreviated 12-char SHA the
    /// history row already carries (matches what the row text displays).
    /// </summary>
    private void OnCopyShaClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.DataContext is not HistoryRow row)
        {
            return;
        }

        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(row.Sha);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        ShowSuccess($"Copied SHA {row.Sha}.");
    }

    // Wraps GitCommit to surface a friendly WhenDisplay for binding without a converter.
    private sealed class HistoryRow(GitCommit commit)
    {
        public string Sha => commit.Sha;
        public string Subject => commit.Subject;
        public string AuthorName => commit.AuthorName;
        public string AuthorEmail => commit.AuthorEmail;
        public string WhenDisplay => commit.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    }

    private void OnChangeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection is the diff target. We keep the checkbox state separate so the
        // user can select a row to view it without including it in the commit.
        if (sender is not ListView list || list.SelectedIndex < 0 || list.SelectedIndex >= _rows.Count)
        {
            return;
        }
        _selectedRow = _rows[list.SelectedIndex];
        _ = RenderDiffForSelectionAsync();
    }

    private void OnChangeItemClicked(object sender, ItemClickEventArgs e)
    {
        // Single-click on the row body shows the diff (same as selection).
        // No navigation here — that's the explicit "Open in editor" link.
    }

    private async Task RenderDiffForSelectionAsync()
    {
        if (_info is null || _selectedRow is null)
        {
            ClearDiff();
            return;
        }

        DiffHeader.Text = _selectedRow.Entry.RelativePath;
        OpenInEditorButton.Visibility = Visibility.Visible;
        DiffPlaceholder.Visibility = Visibility.Collapsed;
        DiffText.Inlines.Clear();
        DiffText.Inlines.Add(new Run { Text = "Loading…", Foreground = new SolidColorBrush(MutedColor) });

        string diff;
        try
        {
            diff = await _gitService.GetDiffAsync(_info, _selectedRow.Entry.RelativePath).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            diff = $"(failed to compute diff: {ex.Message})";
        }

        DiffText.Inlines.Clear();
        if (string.IsNullOrEmpty(diff))
        {
            DiffText.Inlines.Add(new Run { Text = "(no diff)", Foreground = new SolidColorBrush(MutedColor) });
            return;
        }
        RenderColorizedDiff(diff);
    }

    private void RenderColorizedDiff(string diff) => RenderColorizedDiff(diff, DiffText);

    private void RenderColorizedDiff(string diff, TextBlock target)
    {
        target.Inlines.Clear();
        foreach (var rawLine in diff.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var color = LineColor(line);
            target.Inlines.Add(new Run
            {
                Text = line.Length == 0 ? " " : line,
                Foreground = new SolidColorBrush(color),
            });
            target.Inlines.Add(new LineBreak());
        }
    }

    private Color LineColor(string line)
    {
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
            return MutedColor;
        if (line.StartsWith("@@", StringComparison.Ordinal))
            return HunkColor;
        if (line.StartsWith('+'))
            return AddColor;
        if (line.StartsWith('-'))
            return RemoveColor;
        // Default body text: light-grey on dark backgrounds, near-black on light ones.
        return ActualTheme == ElementTheme.Dark
            ? Color.FromArgb(0xFF, 0xD4, 0xD4, 0xD4)
            : Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F);
    }

    private void ClearDiff()
    {
        DiffHeader.Text = "Diff";
        OpenInEditorButton.Visibility = Visibility.Collapsed;
        DiffPlaceholder.Visibility = Visibility.Visible;
        DiffText.Inlines.Clear();
    }

    private async void OnOpenInEditorClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedRow is null) return;
        await OpenInEditorAsync(_selectedRow.Entry.AbsolutePath).ConfigureAwait(true);
    }

    private async Task OpenInEditorAsync(string absolutePath)
    {
        if (App.MainWindowInstance is not { } window) return;

        var normalized = Path.GetFullPath(absolutePath);

        if (normalized.Contains("\\pkgsinfo\\", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/pkgsinfo/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var packages = await _packageService.GetAllPackagesAsync().ConfigureAwait(true);
                var match = packages.FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.FilePath) &&
                    string.Equals(Path.GetFullPath(p.FilePath), normalized, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    window.NavigateToPackage(match);
                    return;
                }
            }
            catch { }
        }
        else if (normalized.Contains("\\manifests\\", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("/manifests/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var manifests = await _manifestService.GetAllManifestsAsync().ConfigureAwait(true);
                var match = manifests.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.FilePath) &&
                    string.Equals(Path.GetFullPath(m.FilePath), normalized, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    window.NavigateToManifest(match);
                    return;
                }
            }
            catch { }
        }
    }

    private async void OnBranchSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBranchChange || _info is null) return;
        if (sender is not ComboBox cb || cb.SelectedItem is not string target) return;
        if (string.Equals(target, _info.Branch, StringComparison.Ordinal)) return;

        BranchPicker.IsEnabled = false;
        try
        {
            var result = await _gitService.CheckoutBranchAsync(_info, target).ConfigureAwait(true);
            if (!result.Success)
            {
                ShowError("Branch switch failed", result.ErrorMessage ?? "Unknown error");
                // Restore selection to current branch.
                _suppressBranchChange = true;
                try { BranchPicker.SelectedItem = _info.Branch; }
                finally { _suppressBranchChange = false; }
            }
            else
            {
                ShowSuccess($"Switched to {target}.");
                await RefreshAsync().ConfigureAwait(true);
                if (App.MainWindowInstance is { } window)
                {
                    await window.RefreshGitIndicatorAsync(_repositoryService.CurrentRepository).ConfigureAwait(true);
                }
            }
        }
        finally
        {
            BranchPicker.IsEnabled = true;
        }
    }

    private async void OnCommitClicked(object sender, RoutedEventArgs e)
    {
        await ExecuteCommitAsync(push: false).ConfigureAwait(true);
    }

    private async void OnCommitPushClicked(object sender, RoutedEventArgs e)
    {
        await ExecuteCommitAsync(push: true).ConfigureAwait(true);
    }

    private async Task ExecuteCommitAsync(bool push)
    {
        if (_info is null) return;

        var paths = _rows.Where(r => r.IsSelected).Select(r => r.Entry.RelativePath).ToList();
        if (paths.Count == 0) return;

        // Read the per-commit toggles. Amend rewrites the previous commit; skip-hooks
        // passes --no-verify. Both default to off, so the typical commit path is
        // unchanged.
        var runHooks = SkipHooksCheckBox.IsChecked != true;
        var amend = AmendCheckBox.IsChecked == true;

        CommitButton.IsEnabled = false;
        CommitPushButton.IsEnabled = false;

        var progress = ShowProgress(push ? "Staging files…" : "Staging files…");
        try
        {
            try
            {
                await _gitService.StageAsync(_info, paths).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ShowError("Staging failed", ex.Message);
                UpdateCommitEnabled();
                return;
            }

            var subject = SubjectBox.Text.Trim();
            var body = string.IsNullOrWhiteSpace(BodyBox.Text) ? null : BodyBox.Text.TrimEnd();

            ProgressTitle.Text = (amend, runHooks) switch
            {
                (true, true) => "Amending previous commit…",
                (true, false) => "Amending previous commit (hooks skipped)…",
                (false, false) => "Committing (hooks skipped)…",
                _ => "Running pre-commit hooks and committing…",
            };

            GitCommitResult commit;
            try
            {
                commit = await _gitService.CommitAsync(_info, subject, body, runHooks, amend, progress).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ShowError("Commit failed", ex.Message);
                UpdateCommitEnabled();
                return;
            }

            if (!commit.Success)
            {
                ShowError("Commit failed", commit.Output);
                UpdateCommitEnabled();
                return;
            }

            if (push)
            {
                ProgressTitle.Text = "Pushing to origin…";
                GitPushResult pushResult;
                try
                {
                    pushResult = await _gitService.PushAsync(_info, progress).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    ShowError("Push failed (commit kept locally)", ex.Message);
                    await RefreshAfterCommitAsync().ConfigureAwait(true);
                    return;
                }

                if (!pushResult.Success)
                {
                    ShowError("Push failed (commit kept locally)", pushResult.Output);
                    await RefreshAfterCommitAsync().ConfigureAwait(true);
                    return;
                }

                ShowSuccess($"Committed {commit.CommitSha ?? "(new)"} and pushed to origin.");
            }
            else
            {
                ShowSuccess($"Committed {commit.CommitSha ?? "(new)"}.");
            }

            SubjectBox.Text = string.Empty;
            BodyBox.Text = string.Empty;
            // Reset Amend after a successful commit so the next one doesn't silently
            // rewrite it; leave Skip-hooks as the user set it since it's not nearly
            // as destructive when accidentally re-applied.
            AmendCheckBox.IsChecked = false;
            await RefreshAfterCommitAsync().ConfigureAwait(true);
        }
        finally
        {
            HideProgress();
        }
    }

    private async Task RefreshAfterCommitAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
        _historyLoaded = false;
        if (BodyPivot.SelectedIndex == 1)
        {
            await LoadHistoryAsync().ConfigureAwait(true);
        }
        if (App.MainWindowInstance is { } window)
        {
            await window.RefreshGitIndicatorAsync(_repositoryService.CurrentRepository).ConfigureAwait(true);
        }
    }

    private void ShowSuccess(string message)
    {
        ResultBar.Severity = InfoBarSeverity.Success;
        ResultBar.Title = "Done";
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }

    private void ShowError(string title, string output)
    {
        ResultBar.Severity = InfoBarSeverity.Error;
        ResultBar.Title = title;
        ResultBar.Message = string.IsNullOrWhiteSpace(output) ? "(no output)" : output;
        ResultBar.IsOpen = true;
    }

    // -------- keyboard shortcuts --------

    /// <summary>
    /// Ctrl+A → check every row. Skipped when focus is in a text input so the
    /// platform's "select all text" behavior still works in the subject/body boxes.
    /// </summary>
    private void OnSelectAllAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused()) return;
        args.Handled = true;
        OnSelectAllClicked(this, new RoutedEventArgs());
    }

    private void OnSelectNoneAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused()) return;
        args.Handled = true;
        OnSelectNoneClicked(this, new RoutedEventArgs());
    }

    private void OnCommitAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!CommitButton.IsEnabled) return;
        args.Handled = true;
        _ = ExecuteCommitAsync(push: false);
    }

    /// <summary>
    /// lazygit-style keyboard handling on the changes list. Single-letter shortcuts
    /// only fire here (when the list has focus), so they never interfere with typing
    /// in the subject / body text boxes.
    /// </summary>
    private void OnChangesListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not ListView list) return;

        switch (e.Key)
        {
            case VirtualKey.Space:
                ToggleSelectedRow(list);
                e.Handled = true;
                return;

            case VirtualKey.A:
                // 'a' toggles all (matches lazygit's stage-all-toggle).
                ToggleAllRows();
                e.Handled = true;
                return;

            case VirtualKey.C:
                // 'c' starts a commit message — focus the subject box.
                SubjectBox.Focus(FocusState.Programmatic);
                e.Handled = true;
                return;

            case VirtualKey.O:
                // 'o' opens the selected file in the matching editor.
                if (list.SelectedIndex >= 0 && list.SelectedIndex < _rows.Count)
                {
                    _ = OpenInEditorAsync(_rows[list.SelectedIndex].Entry.AbsolutePath);
                }
                e.Handled = true;
                return;

            case VirtualKey.R:
                _ = RefreshAsync();
                e.Handled = true;
                return;

            case VirtualKey.J:
                // vim-style down nav.
                if (list.SelectedIndex < _rows.Count - 1)
                {
                    list.SelectedIndex++;
                    list.ScrollIntoView(list.SelectedItem);
                }
                e.Handled = true;
                return;

            case VirtualKey.K:
                // vim-style up nav.
                if (list.SelectedIndex > 0)
                {
                    list.SelectedIndex--;
                    list.ScrollIntoView(list.SelectedItem);
                }
                e.Handled = true;
                return;

            case (VirtualKey)191: // '/' on US layouts; shifted = '?'.
                if (IsShiftDown())
                {
                    _ = ShowHelpAsync();
                    e.Handled = true;
                }
                return;
        }
    }

    private void ToggleSelectedRow(ListView list)
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= _rows.Count) return;

        var row = _rows[list.SelectedIndex];
        row.IsSelected = !row.IsSelected;

        // The item template builds a Grid whose first child is the CheckBox; flip its
        // visible IsChecked to keep the UI in sync without re-rendering the whole list.
        if (list.ContainerFromIndex(list.SelectedIndex) is ListViewItem container &&
            container.ContentTemplateRoot is Grid grid &&
            grid.Children.FirstOrDefault() is CheckBox cb)
        {
            cb.IsChecked = row.IsSelected;
        }
        else
        {
            RenderChanges();
        }

        UpdateCommitEnabled();
    }

    private void ToggleAllRows()
    {
        // lazygit pattern: 'a' toggles between "all selected" and "none selected"
        // based on the current majority state.
        if (_rows.Count == 0) return;
        var allSelected = _rows.All(r => r.IsSelected);
        foreach (var r in _rows) r.IsSelected = !allSelected;
        RenderChanges();
    }

    private static bool IsShiftDown()
    {
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        return (shift & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
    }

    private void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = RefreshAsync();
    }

    /// <summary>
    /// Page-level Space accelerator. Falls through when focus is in a text input
    /// (so typing spaces in subject/body still works) — otherwise toggles the
    /// selected row's checkbox. Works whether the actual focus is on the ListView
    /// container, one of its descendants, or nothing in particular.
    /// </summary>
    private void OnSpaceAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextInputFocused()) return;
        if (ChangesList.SelectedIndex < 0 || ChangesList.SelectedIndex >= _rows.Count) return;
        args.Handled = true;
        ToggleSelectedRow(ChangesList);
    }

    private async void OnHelpClicked(object sender, RoutedEventArgs e)
    {
        await ShowHelpAsync().ConfigureAwait(true);
    }

    private async Task ShowHelpAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Keyboard shortcuts",
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };

        var panel = new StackPanel { Spacing = 6 };
        void AddRow(string keys, string desc)
        {
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var k = new TextBlock
            {
                Text = keys,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x77, 0x9D, 0xFF)),
            };
            Grid.SetColumn(k, 0);
            row.Children.Add(k);
            var d = new TextBlock { Text = desc, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(d, 1);
            row.Children.Add(d);
            panel.Children.Add(row);
        }

        AddRow("space",  "Toggle the focused row");
        AddRow("a",      "Toggle all rows (lazygit-style)");
        AddRow("Ctrl+A", "Select all rows");
        AddRow("Ctrl+Shift+A", "Clear selection");
        AddRow("j / ↓",  "Move selection down");
        AddRow("k / ↑",  "Move selection up");
        AddRow("c",      "Focus the commit subject");
        AddRow("Ctrl+Enter", "Commit");
        AddRow("o",      "Open the focused file in its editor");
        AddRow("r / F5", "Refresh status");
        AddRow("?",      "Show this help");

        dialog.Content = panel;
        await dialog.ShowAsync();
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        return focused is TextBox or RichEditBox or PasswordBox or AutoSuggestBox;
    }

    private sealed class ChangeRow
    {
        public ChangeRow(GitStatusEntry entry)
        {
            Entry = entry;
            // Open with nothing selected — the user opts in to each file. "Select all"
            // is one click away if they want every change in the commit.
            IsSelected = false;
        }

        public GitStatusEntry Entry { get; }
        public bool IsSelected { get; set; }
    }
}
