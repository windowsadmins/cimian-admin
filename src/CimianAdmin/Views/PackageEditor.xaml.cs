namespace CimianAdmin.Views;

using System.Globalization;
using System.Text;
using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class PackageEditor : UserControl
{
    private readonly IPackageService _packageService;
    private readonly ICatalogService _catalogService;
    private Package? _package;
    private bool _suppressDirty;
    private IReadOnlyList<string> _knownCatalogs = [];
    private IReadOnlyList<string> _knownPackageNames = [];
    private string _lastInstallerType = string.Empty;

    private static readonly string[] InstallTypeOptions =
        ["file", "msi", "msix", "registry", "exe", "ps1"];

    public PackageEditor()
        : this(App.Resolve<IPackageService>(), App.Resolve<ICatalogService>())
    {
    }

    public PackageEditor(IPackageService packageService, ICatalogService catalogService)
    {
        ArgumentNullException.ThrowIfNull(packageService);
        ArgumentNullException.ThrowIfNull(catalogService);
        _packageService = packageService;
        _catalogService = catalogService;
        InitializeComponent();
    }

    public bool IsDirty
    {
        get;
        private set
        {
            field = value;
            DirtyIndicator.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            SaveButton.IsEnabled = value && _package is not null;
            RevertButton.IsEnabled = value && _package is not null;
        }
    }

    public async void SetPackage(Package? package)
    {
        _package = package;

        if (package is null)
        {
            EditorRoot.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EditorRoot.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;

        try
        {
            _knownCatalogs = await _catalogService.GetCatalogNamesAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            _knownCatalogs = [];
        }

        try
        {
            var allPackages = await _packageService.GetAllPackagesAsync().ConfigureAwait(true);
            _knownPackageNames = [.. allPackages
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception)
        {
            _knownPackageNames = [];
        }

        // Wire suggestion sources for chip pickers.
        RequiresPicker.Suggestions = _knownPackageNames;
        UpdateForPicker.Suggestions = _knownPackageNames;
        ManagedAppsPicker.Suggestions = _knownPackageNames;
        // No suggestions for free-text fields:
        BlockingPicker.Suggestions = [];
        ManagedProfilesPicker.Suggestions = [];

        Populate(package);
        IsDirty = false;
        StatusBar.IsOpen = false;
    }

    private void Populate(Package package)
    {
        _suppressDirty = true;
        try
        {
            DisplayNameText.Text = package.EffectiveDisplayName;
            VersionText.Text = string.IsNullOrEmpty(package.Version) ? string.Empty : "Version " + package.Version;
            FilePathText.Text = ToRepoRelativePath(package.FilePath);

            NameField.Text = package.Name;
            DisplayNameField.Text = package.DisplayName ?? string.Empty;
            IconNameField.Text = package.IconName ?? string.Empty;
            VersionField.Text = package.Version;
            DescriptionField.Text = package.Description ?? string.Empty;
            DeveloperField.Text = package.Developer ?? string.Empty;
            CategoryField.Text = package.Category ?? string.Empty;
            BuildCatalogChecklist(package.Catalogs);

            InstallerTypeBox.Text = package.Installer?.Type ?? string.Empty;
            _lastInstallerType = InstallerTypeBox.Text;
            InstallerSizeField.Text = package.Installer?.Size?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            InstallerLocationField.Text = package.Installer?.Location ?? string.Empty;
            InstallerHashField.Text = package.Installer?.Hash ?? string.Empty;
            InstallerArgsField.Text = JoinLines(package.Installer?.Arguments);
            InstallerSwitchesField.Text = JoinLines(package.Installer?.Switches);
            InstallerFlagsField.Text = JoinLines(package.Installer?.Flags);
            InstallerSubcommandField.Text = package.Installer?.Subcommand ?? string.Empty;
            InstallerTempDirField.Text = package.Installer?.TempDir ?? string.Empty;
            ApplyInstallerTypeVisibility(package.Installer?.Type);

            UninstallerPathField.Text = package.UninstallerPath ?? string.Empty;
            UninstallerSummary.ItemsSource = (package.Uninstaller ?? [])
                .Select(BuildUninstallerSummary)
                .ToList();
            UninstallerSummaryLabel.Visibility = package.Uninstaller is { Count: > 0 }
                ? Visibility.Visible
                : Visibility.Collapsed;

            UnattendedInstallBox.IsChecked = package.UnattendedInstall;
            UnattendedUninstallBox.IsChecked = package.UnattendedUninstall;
            UninstallableBox.IsChecked = package.Uninstallable ?? false;
            OnDemandBox.IsChecked = package.OnDemand;

            MinOsField.Text = package.MinimumOsVersion ?? string.Empty;
            MaxOsField.Text = package.MaximumOsVersion ?? string.Empty;
            MinCimianField.Text = package.MinimumCimianVersion ?? string.Empty;
            var archs = package.SupportedArchitectures ?? [];
            ArchX64Box.IsChecked = archs.Any(a => string.Equals(a, "x64", StringComparison.OrdinalIgnoreCase));
            ArchArm64Box.IsChecked = archs.Any(a => string.Equals(a, "arm64", StringComparison.OrdinalIgnoreCase));
            BlockingPicker.SetItems(package.BlockingApplications);
            RequiresPicker.SetItems(package.Requires);
            UpdateForPicker.SetItems(package.UpdateFor);
            ManagedProfilesPicker.SetItems(package.ManagedProfiles);
            ManagedAppsPicker.SetItems(package.ManagedApps);

            BuildInstallsRows(package.Installs);

            PreinstallScriptEditor.ScriptText = package.PreinstallScript ?? string.Empty;
            PostinstallScriptEditor.ScriptText = package.PostinstallScript ?? string.Empty;
            InstallCheckScriptEditor.ScriptText = package.InstallCheckScript ?? string.Empty;
            PreuninstallScriptEditor.ScriptText = package.PreuninstallScript ?? string.Empty;
            PostuninstallScriptEditor.ScriptText = package.PostuninstallScript ?? string.Empty;
            UninstallCheckScriptEditor.ScriptText = package.UninstallCheckScript ?? string.Empty;

            WindowStartField.Text = package.InstallWindow?.Start ?? string.Empty;
            WindowEndField.Text = package.InstallWindow?.End ?? string.Empty;
            WindowWeekdaysField.Text = package.InstallWindow?.Weekdays is { Count: > 0 } days
                ? string.Join(", ", days)
                : string.Empty;

            if (package.Metadata is { Count: > 0 } meta)
            {
                MetadataSection.Visibility = Visibility.Visible;
                MetadataText.Text = FormatMetadata(meta);
            }
            else
            {
                MetadataSection.Visibility = Visibility.Collapsed;
                MetadataText.Text = string.Empty;
            }
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    /// <summary>
    /// Hides installer subfields that are irrelevant for the chosen type. Per Cimian
    /// convention product_code/upgrade_code/identity_name belong on installs[] entries,
    /// not the installer block — so they never appear in this section.
    /// </summary>
    private void ApplyInstallerTypeVisibility(string? type)
    {
        var t = (type ?? string.Empty).Trim().ToLowerInvariant();
        switch (t)
        {
            case "msi":
            case "msix":
            case "nupkg":
                InstallerArgsPanel.Visibility = Visibility.Collapsed;
                InstallerSwitchesPanel.Visibility = Visibility.Collapsed;
                InstallerFlagsPanel.Visibility = Visibility.Collapsed;
                InstallerSubcommandPanel.Visibility = Visibility.Collapsed;
                InstallerTempDirPanel.Visibility = Visibility.Collapsed;
                break;
            case "ps1":
            case "bat":
            case "cmd":
                InstallerArgsPanel.Visibility = Visibility.Visible;
                InstallerSwitchesPanel.Visibility = Visibility.Collapsed;
                InstallerFlagsPanel.Visibility = Visibility.Collapsed;
                InstallerSubcommandPanel.Visibility = Visibility.Visible;
                InstallerTempDirPanel.Visibility = Visibility.Collapsed;
                break;
            default: // exe and unknown types — show everything
                InstallerArgsPanel.Visibility = Visibility.Visible;
                InstallerSwitchesPanel.Visibility = Visibility.Visible;
                InstallerFlagsPanel.Visibility = Visibility.Visible;
                InstallerSubcommandPanel.Visibility = Visibility.Visible;
                InstallerTempDirPanel.Visibility = Visibility.Visible;
                break;
        }
    }

    private void OnInstallerTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(InstallerTypeBox.Text))
        {
            _lastInstallerType = InstallerTypeBox.Text;
        }
        ApplyInstallerTypeVisibility(InstallerTypeBox.Text);
        OnFieldChanged(sender, new RoutedEventArgs());
    }

    private void OnInstallerTypeSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (!string.IsNullOrEmpty(args.Text))
        {
            _lastInstallerType = args.Text;
        }
        ApplyInstallerTypeVisibility(args.Text);
        OnFieldChanged(sender, new RoutedEventArgs());
    }

    private void OnInstallerTypeDropDownClosed(object? sender, object e)
    {
        // The editable ComboBox sometimes ends up empty when the user opens the dropdown
        // and dismisses it without selecting. Restore the last good value so the field
        // doesn't disappear after a stray click.
        if (string.IsNullOrEmpty(InstallerTypeBox.Text) && !string.IsNullOrEmpty(_lastInstallerType))
        {
            InstallerTypeBox.Text = _lastInstallerType;
        }
    }

    private void OnInstallerTypeLostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(InstallerTypeBox.Text) && !string.IsNullOrEmpty(_lastInstallerType))
        {
            InstallerTypeBox.Text = _lastInstallerType;
        }
    }

    private void BuildCatalogChecklist(IReadOnlyCollection<string>? selected)
    {
        CatalogsContainer.Children.Clear();
        selected ??= [];
        var union = new HashSet<string>(_knownCatalogs, StringComparer.OrdinalIgnoreCase);
        foreach (var c in selected)
        {
            if (!string.IsNullOrWhiteSpace(c) && !string.Equals(c, "All", StringComparison.OrdinalIgnoreCase))
            {
                union.Add(c);
            }
        }

        var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        foreach (var name in CimianAdmin.Models.CatalogOrdering.Sort(union))
        {
            var box = new CheckBox
            {
                Content = name,
                IsChecked = selectedSet.Contains(name),
                Tag = name,
                MinWidth = 120,
            };
            box.Click += OnFieldChanged;
            CatalogsContainer.Children.Add(box);
        }
    }

    private List<string> ReadCatalogs()
    {
        var result = new List<string>();
        foreach (var child in CatalogsContainer.Children)
        {
            if (child is CheckBox box && box.IsChecked == true && box.Tag is string name)
            {
                result.Add(name);
            }
        }
        return result;
    }

    private void BuildInstallsRows(List<InstallsItem>? items)
    {
        InstallsContainer.Children.Clear();
        items ??= [];
        foreach (var item in items)
        {
            InstallsContainer.Children.Add(BuildInstallRow(item));
        }
        NoInstallsText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border BuildInstallRow(InstallsItem item)
    {
        var border = new Border
        {
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Tag = item,
        };

        var stack = new StackPanel { Spacing = 8 };

        // Type row + Remove button
        var typeRow = new Grid { ColumnSpacing = 8 };
        typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        typeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var typeBox = new ComboBox
        {
            IsEditable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "type",
            Text = item.Type ?? "file",
            // Tag stores both the field marker ("type") and the last non-empty value so we
            // can restore it if the editable ComboBox blanks itself on a stray dropdown click.
            Tag = new InstallTypeBoxState { LastValue = item.Type ?? "file" },
        };
        foreach (var opt in InstallTypeOptions)
        {
            typeBox.Items.Add(opt);
        }
        typeBox.SelectionChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(typeBox.Text)) ((InstallTypeBoxState)typeBox.Tag).LastValue = typeBox.Text;
            RebuildInstallFieldsForRow(stack, typeBox.Text);
        };
        typeBox.TextSubmitted += (_, ev) =>
        {
            if (!string.IsNullOrEmpty(ev.Text)) ((InstallTypeBoxState)typeBox.Tag).LastValue = ev.Text;
            RebuildInstallFieldsForRow(stack, ev.Text);
        };
        typeBox.DropDownClosed += (_, _) =>
        {
            var state = (InstallTypeBoxState)typeBox.Tag;
            if (string.IsNullOrEmpty(typeBox.Text) && !string.IsNullOrEmpty(state.LastValue))
            {
                typeBox.Text = state.LastValue;
            }
        };
        typeBox.LostFocus += (_, _) =>
        {
            var state = (InstallTypeBoxState)typeBox.Tag;
            if (string.IsNullOrEmpty(typeBox.Text) && !string.IsNullOrEmpty(state.LastValue))
            {
                typeBox.Text = state.LastValue;
            }
        };
        typeBox.SelectionChanged += OnFieldChanged;
        typeBox.TextSubmitted += (_, _) => OnFieldChanged(typeBox, new RoutedEventArgs());
        Grid.SetColumn(typeBox, 0);
        typeRow.Children.Add(typeBox);

        var spacer = new TextBlock { Text = string.Empty };
        Grid.SetColumn(spacer, 1);
        typeRow.Children.Add(spacer);

        var removeBtn = new Button
        {
            Content = "Remove",
            Tag = border,
        };
        removeBtn.Click += (_, _) =>
        {
            InstallsContainer.Children.Remove(border);
            NoInstallsText.Visibility = InstallsContainer.Children.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
            OnFieldChanged(removeBtn, new RoutedEventArgs());
        };
        Grid.SetColumn(removeBtn, 2);
        typeRow.Children.Add(removeBtn);

        stack.Children.Add(typeRow);

        // Field area (rebuilt when type changes)
        var fields = new StackPanel { Spacing = 6 };
        fields.Tag = "fields";
        stack.Children.Add(fields);

        border.Child = stack;
        PopulateInstallFields(fields, item, item.Type ?? "file");
        return border;
    }

    private void RebuildInstallFieldsForRow(StackPanel rowStack, string newType)
    {
        // Capture current values from the existing field panel before rebuilding.
        var current = ReadInstallItemFromPanel(rowStack);
        current.Type = newType;
        var fields = (StackPanel)rowStack.Children.Last();
        fields.Children.Clear();
        PopulateInstallFields(fields, current, newType);
    }

    private void PopulateInstallFields(StackPanel target, InstallsItem item, string type)
    {
        target.Children.Clear();
        var t = (type ?? "file").Trim().ToLowerInvariant();

        switch (t)
        {
            case "msi":
                AddInstallField(target, "product_code", item.ProductCode);
                AddInstallField(target, "upgrade_code", item.UpgradeCode);
                AddInstallField(target, "version", item.Version);
                break;
            case "msix":
                AddInstallField(target, "identity_name", item.IdentityName);
                AddInstallField(target, "version", item.Version);
                break;
            case "registry":
                AddInstallField(target, "path", item.Path);
                AddInstallField(target, "version", item.Version);
                break;
            case "file":
            default:
                AddInstallField(target, "path", item.Path);
                AddInstallField(target, "version", item.Version);
                AddInstallField(target, "md5checksum", item.Md5Checksum);
                break;
        }
    }

    private void AddInstallField(StackPanel target, string fieldKey, string? initialValue)
    {
        var label = new TextBlock
        {
            Text = fieldKey,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
        var box = new TextBox
        {
            Text = initialValue ?? string.Empty,
            Tag = fieldKey,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
        };
        box.TextChanged += OnFieldChanged;
        target.Children.Add(label);
        target.Children.Add(box);
    }

    private static InstallsItem ReadInstallItemFromPanel(StackPanel rowStack)
    {
        var item = new InstallsItem();
        var typeRow = (Grid)rowStack.Children[0];
        var typeBox = (ComboBox)typeRow.Children[0];
        item.Type = string.IsNullOrWhiteSpace(typeBox.Text) ? null : typeBox.Text.Trim();

        var fields = (StackPanel)rowStack.Children[1];
        foreach (var child in fields.Children)
        {
            if (child is not TextBox box || box.Tag is not string key)
            {
                continue;
            }
            var v = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
            switch (key)
            {
                case "path": item.Path = v; break;
                case "version": item.Version = v; break;
                case "md5checksum": item.Md5Checksum = v; break;
                case "product_code": item.ProductCode = v; break;
                case "upgrade_code": item.UpgradeCode = v; break;
                case "identity_name": item.IdentityName = v; break;
            }
        }
        return item;
    }

    private List<InstallsItem> ReadInstalls()
    {
        var list = new List<InstallsItem>();
        foreach (var child in InstallsContainer.Children)
        {
            if (child is Border b && b.Child is StackPanel rowStack)
            {
                list.Add(ReadInstallItemFromPanel(rowStack));
            }
        }
        return list;
    }

    private void OnAddInstallClicked(object sender, RoutedEventArgs e)
    {
        var newItem = new InstallsItem { Type = "file" };
        InstallsContainer.Children.Add(BuildInstallRow(newItem));
        NoInstallsText.Visibility = Visibility.Collapsed;
        OnFieldChanged(sender, e);
    }

    private void OnFieldChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressDirty)
        {
            return;
        }
        IsDirty = true;
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_package is null)
        {
            return;
        }

        ApplyEditsTo(_package);

        try
        {
            await _packageService.SavePackageAsync(_package).ConfigureAwait(true);
            IsDirty = false;
            ShowStatus(InfoBarSeverity.Success, "Saved", $"Wrote {_package.FilePath}");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Save failed", ex.Message);
        }
    }

    private void OnRevertClicked(object sender, RoutedEventArgs e)
    {
        if (_package is null)
        {
            return;
        }
        Populate(_package);
        IsDirty = false;
        StatusBar.IsOpen = false;
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (_package is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete this pkginfo?",
            Content = $"This permanently deletes:\n\n{_package.FilePath}\n\nThe installer file in pkgs/ is left in place.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _packageService.DeletePackageAsync(_package, deleteInstaller: false).ConfigureAwait(true);
            _package = null;
            EditorRoot.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            ShowStatus(InfoBarSeverity.Success, "Deleted", "Pkginfo file removed.");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Delete failed", ex.Message);
        }
    }

    private void ApplyEditsTo(Package package)
    {
        package.Name = NameField.Text.Trim();
        package.DisplayName = NullIfBlank(DisplayNameField.Text);
        package.IconName = NullIfBlank(IconNameField.Text);
        // package.Identifier is preserved as loaded; the UI no longer exposes it because
        // Cimian's runtime never reads it (per Munki/Cimian field-comparison research).
        package.Version = VersionField.Text.Trim();
        package.Description = NullIfBlank(DescriptionField.Text);
        package.Developer = NullIfBlank(DeveloperField.Text);
        package.Category = NullIfBlank(CategoryField.Text);
        package.Catalogs = ReadCatalogs();

        var installerType = NullIfBlank(InstallerTypeBox.Text);
        var installerLocation = NullIfBlank(InstallerLocationField.Text);
        var installerHash = NullIfBlank(InstallerHashField.Text);
        var installerArgs = SplitLinesOrNull(InstallerArgsField.Text);
        var installerSwitches = SplitLinesOrNull(InstallerSwitchesField.Text);
        var installerFlags = SplitLinesOrNull(InstallerFlagsField.Text);
        var installerSubcommand = NullIfBlank(InstallerSubcommandField.Text);
        var installerTempDir = NullIfBlank(InstallerTempDirField.Text);
        long? installerSize = long.TryParse(InstallerSizeField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sz) ? sz : null;

        if (installerType is null && installerLocation is null && installerHash is null && installerSize is null
            && installerArgs is null && installerSwitches is null && installerFlags is null
            && installerSubcommand is null && installerTempDir is null)
        {
            package.Installer = null;
        }
        else
        {
            package.Installer ??= new Installer();
            package.Installer.Type = installerType;
            package.Installer.Location = installerLocation;
            package.Installer.Hash = installerHash;
            package.Installer.Size = installerSize;
            package.Installer.Arguments = installerArgs;
            package.Installer.Switches = installerSwitches;
            package.Installer.Flags = installerFlags;
            package.Installer.Subcommand = installerSubcommand;
            package.Installer.TempDir = installerTempDir;
            // product_code / upgrade_code / identity_name go in installs[], so keep
            // whatever was already on the model unchanged.
        }

        package.UninstallerPath = NullIfBlank(UninstallerPathField.Text);

        package.UnattendedInstall = UnattendedInstallBox.IsChecked ?? false;
        package.UnattendedUninstall = UnattendedUninstallBox.IsChecked ?? false;
        // Tri-state Uninstallable: only emit explicit `true`. Unchecked → null (omit).
        package.Uninstallable = UninstallableBox.IsChecked == true ? true : null;
        package.OnDemand = OnDemandBox.IsChecked ?? false;
        // package.InstallerType (top-level) is preserved as loaded; the UI no longer
        // exposes it because Cimian treats installer.type as canonical.

        package.MinimumOsVersion = NullIfBlank(MinOsField.Text);
        package.MaximumOsVersion = NullIfBlank(MaxOsField.Text);
        package.MinimumCimianVersion = NullIfBlank(MinCimianField.Text);
        var archs = new List<string>();
        if (ArchX64Box.IsChecked == true) archs.Add("x64");
        if (ArchArm64Box.IsChecked == true) archs.Add("arm64");
        package.SupportedArchitectures = archs.Count == 0 ? null : archs;
        package.BlockingApplications = NullIfEmpty(BlockingPicker.GetItems());
        package.Requires = NullIfEmpty(RequiresPicker.GetItems());
        package.UpdateFor = NullIfEmpty(UpdateForPicker.GetItems());
        package.ManagedProfiles = NullIfEmpty(ManagedProfilesPicker.GetItems());
        package.ManagedApps = NullIfEmpty(ManagedAppsPicker.GetItems());

        var installs = ReadInstalls();
        package.Installs = installs.Count == 0 ? null : installs;

        package.PreinstallScript = NullIfBlank(PreinstallScriptEditor.ScriptText);
        package.PostinstallScript = NullIfBlank(PostinstallScriptEditor.ScriptText);
        package.InstallCheckScript = NullIfBlank(InstallCheckScriptEditor.ScriptText);
        package.PreuninstallScript = NullIfBlank(PreuninstallScriptEditor.ScriptText);
        package.PostuninstallScript = NullIfBlank(PostuninstallScriptEditor.ScriptText);
        package.UninstallCheckScript = NullIfBlank(UninstallCheckScriptEditor.ScriptText);

        var winStart = NullIfBlank(WindowStartField.Text);
        var winEnd = NullIfBlank(WindowEndField.Text);
        // Weekdays now accepts comma- or whitespace-separated tokens on a single line.
        var winWeekdays = SplitTokensOrNull(WindowWeekdaysField.Text);
        if (winStart is null && winEnd is null && winWeekdays is null)
        {
            package.InstallWindow = null;
        }
        else
        {
            package.InstallWindow ??= new InstallWindow();
            package.InstallWindow.Start = winStart;
            package.InstallWindow.End = winEnd;
            package.InstallWindow.Weekdays = winWeekdays;
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private static string JoinLines(List<string>? list) =>
        list is null || list.Count == 0 ? string.Empty : string.Join(Environment.NewLine, list);

    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }
        return [.. text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static s => s.Trim())
            .Where(static s => s.Length > 0)];
    }

    private static List<string>? SplitLinesOrNull(string? text)
    {
        var list = SplitLines(text);
        return list.Count == 0 ? null : list;
    }

    private static string? NullIfBlank(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string ToRepoRelativePath(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return string.Empty;
        var repo = App.Resolve<IRepositoryService>().CurrentRepository;
        if (repo is null || string.IsNullOrEmpty(repo.RootPath)) return fullPath;
        if (!fullPath.StartsWith(repo.RootPath, StringComparison.OrdinalIgnoreCase)) return fullPath;
        var rel = Path.GetRelativePath(repo.RootPath, fullPath);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string BuildUninstallerSummary(Installer step)
    {
        var bits = new List<string>();
        if (!string.IsNullOrEmpty(step.Type)) bits.Add($"type: {step.Type}");
        if (!string.IsNullOrEmpty(step.Location)) bits.Add($"location: {step.Location}");
        if (!string.IsNullOrEmpty(step.IdentityName)) bits.Add($"identity_name: {step.IdentityName}");
        if (!string.IsNullOrEmpty(step.ProductCode)) bits.Add($"product_code: {step.ProductCode}");
        if (step.Arguments is { Count: > 0 } args) bits.Add($"arguments: {string.Join(' ', args)}");
        return string.Join("  •  ", bits);
    }

    private static string FormatMetadata(Dictionary<string, string?> metadata)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in metadata)
        {
            sb.Append(k).Append(": ").AppendLine(v?.ToString() ?? string.Empty);
        }
        return sb.ToString().TrimEnd();
    }

    private static List<string>? NullIfEmpty(List<string> list) =>
        list.Count == 0 ? null : list;

    private static List<string>? SplitTokensOrNull(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var list = text
            .Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static s => s.Trim())
            .Where(static s => s.Length > 0)
            .ToList();
        return list.Count == 0 ? null : list;
    }

    /// <summary>Mutable state stashed on each install-row type ComboBox via Tag.</summary>
    private sealed class InstallTypeBoxState
    {
        public string LastValue { get; set; } = string.Empty;
    }
}
