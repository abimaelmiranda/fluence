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
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workbench;
using Fluence.Core.ViewModels;
using Fluence.Modules.DotnetCli.Services;

namespace Fluence.Modules.DotnetCli.ViewModels;

public sealed partial class NewProjectWizardViewModel : ViewModelBase
{
    private readonly IWorkspaceDialogService _dialogs;
    private readonly IOutputChannelService _output;
    private readonly IProcessHost _processHost;
    private readonly IShellEventBus _events;
    private readonly IDotnetSdkProvisioningService _sdk;
    private readonly IUserNotificationService _notifications;
    private bool _isInitializing;
    private bool _syncSolutionNameWithProjectName = true;

    public NewProjectWizardViewModel(
        IWorkspaceDialogService dialogs,
        IOutputChannelService output,
        IProcessHost processHost,
        IShellEventBus events,
        IDotnetSdkProvisioningService sdk,
        IUserNotificationService notifications)
    {
        _dialogs = dialogs;
        _output = output;
        _processHost = processHost;
        _events = events;
        _sdk = sdk;
        _notifications = notifications;
        _isInitializing = true;

        Templates =
        [
            new("Console App", "console", "Command-line application"),
            new("Class Library", "classlib", "Reusable C# library"),
            new("Worker Service", "worker", "Background service"),
            new("ASP.NET Core Web API", "webapi", "HTTP API service"),
            new("ASP.NET Core MVC", "mvc", "MVC web application"),
            new("Razor Pages", "webapp", "Page-focused web app"),
            new("Blazor Web App", "blazor", "Interactive web UI"),
            new("xUnit Test Project", "xunit", "xUnit test project"),
            new("NUnit Test Project", "nunit", "NUnit test project"),
            new("MSTest Test Project", "mstest", "MSTest test project"),
        ];

        Frameworks = ["net10.0", "net9.0", "net8.0"];
        SelectedTemplate = Templates[0];
        SelectedFramework = Frameworks[0];
        ProjectName = "MyProject";
        SolutionName = "MyProject";
        Location = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source",
            "repos");
        _isInitializing = false;
    }

    public ObservableCollection<DotnetTemplateOption> Templates { get; }

    public ObservableCollection<string> Frameworks { get; }

    [ObservableProperty]
    private DotnetTemplateOption? _selectedTemplate;

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
    private string _status = "Choose a template and location.";

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
    private void OpenSdkSetup()
    {
        _events.Publish(new DotnetSdkSetupRequestedEvent());
    }

    [RelayCommand]
    private async Task CreateProjectAsync(CancellationToken cancellationToken)
    {
        if (!Validate(out var projectRoot))
            return;

        _output.Clear(OutputChannelIds.Run);

        var sdkStatus = await _sdk.GetStatusAsync(cancellationToken);
        if (!sdkStatus.IsDotnetAvailable || sdkStatus.InstalledSdks.Count == 0)
        {
            _events.Publish(new DotnetSdkSetupRequestedEvent());
            Status = ".NET SDK is required before creating projects.";
            return;
        }

        IsRunning = true;
        Status = "Creating project...";

        try
        {
            Directory.CreateDirectory(Location);
            var template = SelectedTemplate!;
            var newProjectArguments = new List<string> { "new", template.ShortName, "-n", ProjectName, "-o", projectRoot };
            if (template.SupportsFramework && !string.IsNullOrWhiteSpace(SelectedFramework))
            {
                newProjectArguments.Add("-f");
                newProjectArguments.Add(SelectedFramework);
            }

            _events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
            await RunDotnetAsync(
                newProjectArguments,
                Location,
                cancellationToken);

            string? solutionPath = null;
            if (CreateSolution)
            {
                var solutionName = string.IsNullOrWhiteSpace(SolutionName) ? ProjectName : SolutionName.Trim();
                var solutionLocation = PlaceSolutionInProjectFolder ? projectRoot : Location;
                await RunDotnetAsync(
                    ["new", "sln", "-n", solutionName, "-o", solutionLocation],
                    solutionLocation,
                    cancellationToken);
                solutionPath = ResolveSolutionFile(solutionLocation, solutionName);
                if (solutionPath is null)
                    throw new FileNotFoundException($"The solution file was not created in '{solutionLocation}'.");
                await RunDotnetAsync(
                    ["sln", solutionPath, "add", FindProjectFile(projectRoot) ?? projectRoot],
                    solutionLocation,
                    cancellationToken);
            }

            Status = "Project created.";

            if (OpenAfterCreate)
            {
                if (solutionPath is not null && File.Exists(solutionPath))
                    _events.Publish(new OpenSolutionRequestedEvent(solutionPath));
                else
                    _events.Publish(new OpenFolderRequestedEvent(projectRoot));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "Unable to create project.";
            _notifications.ShowError("Unable to create project", ex.Message);
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
            Status = "Select a template.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProjectName) || ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Status = "Enter a valid project name.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Location))
        {
            Status = "Choose a project location.";
            return false;
        }

        projectRoot = Path.Combine(Location, ProjectName.Trim());
        if (Directory.Exists(projectRoot) && Directory.EnumerateFileSystemEntries(projectRoot).Any())
        {
            Status = "The target project folder is not empty.";
            return false;
        }

        return true;
    }

    private static string? FindProjectFile(string projectRoot)
    {
        return Directory.Exists(projectRoot)
            ? Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault()
            : null;
    }

    private static string? ResolveSolutionFile(string location, string solutionName)
    {
        var candidates = new[]
        {
            Path.Combine(location, $"{solutionName}.slnx"),
            Path.Combine(location, $"{solutionName}.sln"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task RunDotnetAsync(IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var dotnet = await _sdk.ResolveDotnetExecutableAsync(cancellationToken);
        await _output.WriteAsync(OutputChannelIds.Run, $"> {DotnetCommandLine.Format(dotnet, arguments)}{Environment.NewLine}", cancellationToken: cancellationToken);

        await _processHost.RunAsync(
            dotnet,
            arguments,
            workingDirectory,
            line => _ = _output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = _output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputChannelEntryKind.Error),
            cancellationToken);
    }
}
