using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Services.File;
using Fluence.Core.Models.Output;
using Fluence.Core.ViewModels;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.NuGetExplorer.Abstractions;
using Fluence.Modules.NuGetExplorer.Models;
using Fluence.Modules.NuGetExplorer.Services;
using Fluence.Core.Events.Workspace;
using NuGet.Versioning;

namespace Fluence.Modules.NuGetExplorer.ViewModels;

public sealed partial class NuGetExplorerViewModel : ViewModelBase
{
    private const string ToolTabIdPrefix = "tool://fluence/nuget/";

    private readonly IWorkspaceContext _workspace;
    private readonly INuGetPackageSource _packageSource;
    private readonly INuGetProjectService _projectService;
    private readonly PackageIconLoader _icons;
    private readonly IUserNotificationService _notifications;
    private readonly IShellEventBus _eventBus;
    private readonly IOutputChannelService _output;
    private readonly ILocalizationService _loc;
    private string? _solutionPath;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _includePrerelease;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _usesCentralPackageManagement;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private NuGetPackageSearchItem? _selectedBrowsePackage;

    [ObservableProperty]
    private string? _selectedVersion;

    [ObservableProperty]
    private NuGetInstalledPackage? _selectedInstalledPackage;

    [ObservableProperty]
    private NuGetPackageUpdate? _selectedUpdate;

    public NuGetExplorerViewModel(
        IWorkspaceContext workspace,
        INuGetPackageSource packageSource,
        INuGetProjectService projectService,
        PackageIconLoader icons,
        IUserNotificationService notifications,
        IShellEventBus eventBus,
        IOutputChannelService output,
        ILocalizationService loc)
    {
        _workspace = workspace;
        _packageSource = packageSource;
        _projectService = projectService;
        _icons = icons;
        _notifications = notifications;
        _eventBus = eventBus;
        _output = output;
        _loc = loc;
    }

    public ObservableCollection<NuGetPackageSearchItem> BrowseResults { get; } = [];

    public ObservableCollection<string> AvailableVersions { get; } = [];

    public ObservableCollection<NuGetProjectSelection> TargetProjects { get; } = [];

    public ObservableCollection<NuGetInstalledPackage> InstalledPackages { get; } = [];

    public ObservableCollection<NuGetPackageUpdate> UpdateCandidates { get; } = [];

    public bool HasSelectedBrowsePackage => SelectedBrowsePackage is not null;

    public string SelectedBrowsePackageTitle => SelectedBrowsePackage?.Id ?? _loc.Get("NuGetExplorer.Label.SelectPackage");

    public bool CanModifyPackages => !UsesCentralPackageManagement;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));

    public void OpenForSolution(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

        _solutionPath = solutionPath;
        WriteOutput($"[NuGetExplorer] Opening package manager for {Path.GetFileName(solutionPath)}\r\n");
        _workspace.OpenToolTab(GetToolTabId(solutionPath), _loc.Get("NuGetExplorer.Title"), this);
        _ = RefreshAsync();
    }

    partial void OnSelectedBrowsePackageChanged(NuGetPackageSearchItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedBrowsePackage));
        OnPropertyChanged(nameof(SelectedBrowsePackageTitle));
        
        _ = LoadVersionsForSelectedPackageAsync();
    }


    partial void OnUsesCentralPackageManagementChanged(bool value)
    {
        OnPropertyChanged(nameof(CanModifyPackages));
    }

    partial void OnIncludePrereleaseChanged(bool value)
    {
        _ = RefreshUpdatesAsync();
        if (SelectedBrowsePackage is not null)
        {
            _ = LoadVersionsForSelectedPackageAsync();
        }
    }

    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        await RunBusyAsync(async token =>
        {
            BrowseResults.Clear();
            var results = await _packageSource.SearchAsync(SearchQuery, IncludePrerelease, token);
            foreach (var result in results)
            {
                var item = new NuGetPackageSearchItem(result);
                BrowseResults.Add(item);
                _ = LoadIconAsync(item, token);
            }

            StatusMessage = string.Format(_loc.Get("NuGetExplorer.Status.FoundPackages"), BrowseResults.Count);
        }, cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_solutionPath))
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCts.Token;

        await RunBusyAsync(async innerToken =>
        {
            WriteOutput("[NuGetExplorer] Loading package data\r\n");
            UsesCentralPackageManagement = _projectService.UsesCentralPackageManagement(_solutionPath);
            await LoadProjectsAsync(innerToken);
            await LoadInstalledPackagesAsync(innerToken);
            await LoadUpdatesAsync(innerToken);
            UpdateAlreadyInstalledFlags();

            StatusMessage = UsesCentralPackageManagement
                ? _loc.Get("NuGetExplorer.Status.CpmDetected")
                : _loc.Get("NuGetExplorer.Status.Loaded");
        }, token);
    }

    [RelayCommand]
    private async Task InstallSelectedPackageAsync(CancellationToken cancellationToken)
    {
        if (!CanModifyPackages || SelectedBrowsePackage is null || string.IsNullOrWhiteSpace(SelectedVersion))
        {
            NotifyBlockedIfNeeded();
            return;
        }

        var selectedProjects = TargetProjects.Where(project => project.IsSelected).ToArray();
        if (selectedProjects.Length == 0)
        {
            _notifications.ShowWarning(_loc.Get("NuGetExplorer.Title"), _loc.Get("NuGetExplorer.Warning.SelectAtLeastOneProject"));
            return;
        }

        await RunBusyAsync(async token =>
        {
            foreach (var project in selectedProjects)
            {
                token.ThrowIfCancellationRequested();
                await _projectService.InstallPackageAsync(
                    project.ProjectPath,
                    SelectedBrowsePackage.Package.Id,
                    SelectedVersion,
                    token);
            }

            await LoadInstalledPackagesAsync(token);
            await LoadUpdatesAsync(token);
            UpdateAlreadyInstalledFlags();
            StatusMessage = string.Format(_loc.Get("NuGetExplorer.Status.Installed"), SelectedBrowsePackage.Id, SelectedVersion);
        }, cancellationToken);
    }

    [RelayCommand]
    private async Task RemoveSelectedPackageAsync(CancellationToken cancellationToken)
    {
        var package = SelectedInstalledPackage;
        if (!CanModifyPackages || package is null)
        {
            NotifyBlockedIfNeeded();
            return;
        }

        await RunBusyAsync(async token =>
        {
            await _projectService.RemovePackageAsync(
                package.ProjectPath,
                package.Id,
                token);

            await LoadInstalledPackagesAsync(token);
            await LoadUpdatesAsync(token);
            StatusMessage = string.Format(_loc.Get("NuGetExplorer.Status.Removed"), package.Id);
        }, cancellationToken);
    }

    [RelayCommand]
    private async Task UpdateSelectedPackageAsync(CancellationToken cancellationToken)
    {
        if (!CanModifyPackages || SelectedUpdate is null)
        {
            NotifyBlockedIfNeeded();
            return;
        }

        var update = SelectedUpdate;
        await RunBusyAsync(async token =>
        {
            foreach (var package in update.InstalledPackages)
            {
                token.ThrowIfCancellationRequested();
                await _projectService.InstallPackageAsync(
                    package.ProjectPath,
                    package.Id,
                    update.LatestVersion,
                    token);
            }

            await LoadInstalledPackagesAsync(token);
            await LoadUpdatesAsync(token);
            StatusMessage = string.Format(_loc.Get("NuGetExplorer.Status.Updated"), update.Id, update.LatestVersion);
        }, cancellationToken);
    }

    private async Task LoadProjectsAsync(CancellationToken cancellationToken)
    {
        TargetProjects.Clear();
        if (string.IsNullOrWhiteSpace(_solutionPath))
        {
            return;
        }

        var projects = await _projectService.GetProjectsAsync(_solutionPath, cancellationToken);
        foreach (var project in projects)
        {
            TargetProjects.Add(new NuGetProjectSelection(project));
        }
    }

    private async Task LoadInstalledPackagesAsync(CancellationToken cancellationToken)
    {
        InstalledPackages.Clear();
        if (string.IsNullOrWhiteSpace(_solutionPath))
        {
            return;
        }

        var packages = await _projectService.GetInstalledPackagesAsync(_solutionPath, cancellationToken);
        foreach (var package in packages)
        {
            InstalledPackages.Add(package);
        }
    }

    private async Task RefreshUpdatesAsync()
    {
        if (string.IsNullOrWhiteSpace(_solutionPath))
        {
            return;
        }

        await RunBusyAsync(LoadUpdatesAsync, CancellationToken.None);
    }

    private async Task LoadUpdatesAsync(CancellationToken cancellationToken)
    {
        UpdateCandidates.Clear();
        var updates = new List<NuGetPackageUpdate>();

        foreach (var group in InstalledPackages.GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latestVersion = await _packageSource.GetLatestVersionAsync(group.Key, IncludePrerelease, cancellationToken);
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                continue;
            }

            var packagesToUpdate = group
                .Where(package => IsNewerVersion(latestVersion, package.Version))
                .ToArray();

            if (packagesToUpdate.Length > 0)
            {
                updates.Add(new NuGetPackageUpdate(group.Key, latestVersion, packagesToUpdate));
            }
        }

        foreach (var update in updates.OrderBy(update => update.Id, StringComparer.OrdinalIgnoreCase))
        {
            UpdateCandidates.Add(update);
        }
    }

    private async Task LoadVersionsForSelectedPackageAsync()
    {
        AvailableVersions.Clear();
        SelectedVersion = null;
        if (SelectedBrowsePackage is null)
        {
            foreach (var project in TargetProjects)
                project.IsAlreadyInstalled = false;
            return;
        }

        await RunBusyAsync(async token =>
        {
            var versions = await _packageSource.GetVersionsAsync(SelectedBrowsePackage.Id, IncludePrerelease, token);
            foreach (var version in versions)
            {
                AvailableVersions.Add(version);
            }

            SelectedVersion = AvailableVersions.FirstOrDefault();
            UpdateAlreadyInstalledFlags();
        }, CancellationToken.None);
    }

    private async Task RunBusyAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            StatusMessage = null;
            await operation(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException)
        {
            StatusMessage = ex.Message;
            WriteOutput($"[NuGetExplorer] Failed: {ex.Message}\r\n", OutputChannelEntryKind.Error);
            _notifications.ShowError(_loc.Get("NuGetExplorer.Title"), ex.Message);
        }
        catch (Exception ex)
        {
            StatusMessage = _loc.Get("NuGetExplorer.Status.UnableToReachFeed");
            WriteOutput($"[NuGetExplorer] Crashed: {ex.Message}\r\n", OutputChannelEntryKind.Error);
            _notifications.ShowError(_loc.Get("NuGetExplorer.Title"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadIconAsync(NuGetPackageSearchItem item, CancellationToken cancellationToken)
    {
        try
        {
            var icon = await _icons.LoadAsync(item.IconUrl, cancellationToken);
            if (icon is not null)
            {
                Dispatcher.UIThread.Post(() => item.Icon = icon);
            }
        }
        catch
        {
        }
    }

    private void UpdateAlreadyInstalledFlags()
    {
        var packageId = SelectedBrowsePackage?.Id;
        foreach (var project in TargetProjects)
        {
            project.IsAlreadyInstalled = packageId is not null &&
                InstalledPackages.Any(p =>
                    string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.ProjectPath, project.ProjectPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void NotifyBlockedIfNeeded()
    {
        if (UsesCentralPackageManagement)
        {
            _notifications.ShowWarning(
                _loc.Get("NuGetExplorer.Title"),
                _loc.Get("NuGetExplorer.Warning.CpmEditingDisabled"));
        }
    }

    private void NotifySolutionChanged()
    {
        _eventBus.Publish(new RefreshSolutionViewRequestedEvent());
    }

    private static bool IsNewerVersion(string candidateVersion, string currentVersion)
    {
        return NuGetVersion.TryParse(candidateVersion, out var candidate) &&
               NuGetVersion.TryParse(currentVersion, out var current) &&
               VersionComparer.Default.Compare(candidate, current) > 0;
    }

    private static string GetToolTabId(string solutionPath)
        => $"{ToolTabIdPrefix}{Path.GetFullPath(solutionPath)}";

    private void WriteOutput(string text, OutputChannelEntryKind kind = OutputChannelEntryKind.Information) =>
        _ = _output.WriteAsync(OutputChannelIds.Output, text, kind);
}
