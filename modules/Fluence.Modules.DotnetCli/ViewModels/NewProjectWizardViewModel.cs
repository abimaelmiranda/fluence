using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Projects;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Output;
using Fluence.Core.Events.Build;
using Fluence.Core.Events.Ui;
using Fluence.Core.Events.Workspace;
using Fluence.Core.ViewModels;
using Fluence.Core.Models.Workbench;

namespace Fluence.Modules.DotnetCli.ViewModels;

public sealed partial class NewProjectWizardViewModel : ViewModelBase
{
    private readonly IWorkspaceContext _workspace;
    private readonly IWorkspaceDialogService _dialogs;
    private readonly IShellEventBus _events;
    private readonly IUserNotificationService _notifications;
    private readonly ILocalizationService _loc;
    private readonly ILanguageProfileRegistry _languageProfiles;
    private readonly IOutputChannelService _output;
    private bool _isInitializing;
    private bool _syncSolutionNameWithProjectName = true;

    public NewProjectWizardViewModel(
        IWorkspaceContext workspace,
        IWorkspaceDialogService dialogs,
        IShellEventBus events,
        IUserNotificationService notifications,
        ILocalizationService loc,
        ILanguageProfileRegistry languageProfiles,
        IOutputChannelService output,
        IEnumerable<IProjectTemplateProvider> providers)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _events = events;
        _notifications = notifications;
        _loc = loc;
        _languageProfiles = languageProfiles;
        _output = output;

        _isInitializing = true;
        Providers = new ObservableCollection<IProjectTemplateProvider>(
            providers
                .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        Templates = [];
        Frameworks = ["net10.0", "net9.0", "net8.0"];
        ProjectName = "MyProject";
        SolutionName = "MyProject";
        Location = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source",
            "repos");

        SelectedProvider = ResolveInitialProvider();
        SelectedFramework = Frameworks[0];
        OpenAfterCreate = true;
        Status = _loc.Get("DotnetCli.Status.ChooseTemplateAndLocation");
        _isInitializing = false;
    }

    public ObservableCollection<IProjectTemplateProvider> Providers { get; }

    public ObservableCollection<ProjectTemplateDefinition> Templates { get; }

    public ObservableCollection<string> Frameworks { get; }

    [ObservableProperty]
    private IProjectTemplateProvider? _selectedProvider;

    [ObservableProperty]
    private ProjectTemplateDefinition? _selectedTemplate;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private string _selectedFramework = string.Empty;

    [ObservableProperty]
    private bool _createSolution = true;

    [ObservableProperty]
    private string _solutionName = string.Empty;

    [ObservableProperty]
    private bool _placeSolutionInProjectFolder;

    [ObservableProperty]
    private bool _openAfterCreate = true;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = string.Empty;

    public bool CanOpenProviderSetup => SelectedProvider?.ProviderId == "csharp";

    public bool CanCreateSolution => SelectedProvider?.SupportsSolutionCreation == true;

    public bool CanSelectFramework => SelectedTemplate?.SupportsFramework == true;

    partial void OnSelectedProviderChanged(IProjectTemplateProvider? value)
    {
        if (value is null)
            return;

        Templates.Clear();
        foreach (var template in value.Templates)
            Templates.Add(template);

        SelectedTemplate = Templates.FirstOrDefault();
        CreateSolution = value.SupportsSolutionCreation;
        if (!value.SupportsSolutionCreation)
        {
            SolutionName = string.Empty;
            PlaceSolutionInProjectFolder = false;
        }

        OnPropertyChanged(nameof(CanOpenProviderSetup));
        OnPropertyChanged(nameof(CanCreateSolution));
    }

    partial void OnSelectedTemplateChanged(ProjectTemplateDefinition? value) =>
        OnPropertyChanged(nameof(CanSelectFramework));

    partial void OnProjectNameChanged(string value)
    {
        if (_isInitializing || !_syncSolutionNameWithProjectName)
            return;

        SolutionName = value;
    }

    partial void OnSolutionNameChanged(string value)
    {
        if (_isInitializing)
            return;

        _syncSolutionNameWithProjectName = string.Equals(value, ProjectName, StringComparison.Ordinal);
    }

    [RelayCommand]
    private async Task BrowseLocationAsync(CancellationToken cancellationToken)
    {
        var folder = await _dialogs.PickFolderAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(folder))
            Location = folder;
    }

    [RelayCommand]
    private void OpenProviderSetup()
    {
        if (SelectedProvider?.ProviderId == "csharp")
            _events.Publish(new DotnetSdkSetupRequestedEvent());
    }

    [RelayCommand]
    private async Task CreateProjectAsync(CancellationToken cancellationToken)
    {
        if (!Validate(out var projectRoot))
            return;

        if (SelectedProvider is null || SelectedTemplate is null)
        {
            Status = _loc.Get("DotnetCli.Validation.SelectTemplate");
            return;
        }

        IsRunning = true;
        Status = _loc.Get("DotnetCli.Status.CreatingProject");

        try
        {
            var request = new ProjectCreationRequest(
                ProjectName.Trim(),
                Location.Trim(),
                SelectedTemplate,
                SelectedTemplate.SupportsFramework && !string.IsNullOrWhiteSpace(SelectedFramework) ? SelectedFramework : null,
                string.IsNullOrWhiteSpace(SolutionName) ? ProjectName.Trim() : SolutionName.Trim(),
                CreateSolution && SelectedProvider.SupportsSolutionCreation,
                PlaceSolutionInProjectFolder,
                OpenAfterCreate,
                Options: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            _output.Clear(OutputChannelIds.Run);
            _events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
            var result = await SelectedProvider.CreateAsync(request, AppendOutput, cancellationToken).ConfigureAwait(false);
            Status = _loc.Get("DotnetCli.Status.ProjectCreated");

            if (!OpenAfterCreate)
                return;

            if (!string.IsNullOrWhiteSpace(result.SolutionPath) && File.Exists(result.SolutionPath))
            {
                _events.Publish(new OpenSolutionRequestedEvent(result.SolutionPath));
                return;
            }

            _events.Publish(new OpenFolderRequestedEvent(result.ProjectRoot));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = _loc.Get("DotnetCli.Status.UnableToCreateProject");
            _notifications.ShowError(_loc.Get("DotnetCli.Error.CreateProjectTitle"), ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool Validate(out string projectRoot)
    {
        projectRoot = string.Empty;

        if (SelectedTemplate is null)
        {
            Status = _loc.Get("DotnetCli.Validation.SelectTemplate");
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProjectName) || ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Status = _loc.Get("DotnetCli.Validation.ValidProjectName");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Location))
        {
            Status = _loc.Get("DotnetCli.Validation.ProjectLocation");
            return false;
        }

        projectRoot = Path.Combine(Location.Trim(), ProjectName.Trim());
        if (Directory.Exists(projectRoot) && Directory.EnumerateFileSystemEntries(projectRoot).Any())
        {
            Status = _loc.Get("DotnetCli.Validation.TargetFolderNotEmpty");
            return false;
        }

        return true;
    }

    private IProjectTemplateProvider ResolveInitialProvider()
    {
        var workspaceLanguage = _languageProfiles.DetectWorkspaceLanguage(_workspace);
        return Providers.FirstOrDefault(provider => provider.CanHandleWorkspace(workspaceLanguage))
            ?? Providers.FirstOrDefault(provider => provider.ProviderId == "csharp")
            ?? Providers.FirstOrDefault()
            ?? throw new InvalidOperationException("No project template providers were registered.");
    }

    private void AppendOutput(string line)
    {
        _ = _output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine);
    }
}
