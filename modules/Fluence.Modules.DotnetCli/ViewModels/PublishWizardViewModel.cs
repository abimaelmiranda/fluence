using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workbench;
using Fluence.Core.ViewModels;
using Fluence.Modules.DotnetCli.Services;

namespace Fluence.Modules.DotnetCli.ViewModels;

public sealed partial class PublishWizardViewModel : ViewModelBase
{
    private readonly IWorkspaceContext _workspace;
    private readonly IWorkspaceDialogService _dialogs;
    private readonly IOutputChannelService _output;
    private readonly IProcessHost _processHost;
    private readonly IShellEventBus _events;
    private readonly IDotnetSdkProvisioningService _sdk;
    private readonly IUserNotificationService _notifications;
    private readonly ILaunchSettingsService _launchSettingsService;

    public PublishWizardViewModel(
        IWorkspaceContext workspace,
        IWorkspaceDialogService dialogs,
        IOutputChannelService output,
        IProcessHost processHost,
        IShellEventBus events,
        IDotnetSdkProvisioningService sdk,
        IUserNotificationService notifications,
        ILaunchSettingsService launchSettingsService)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _output = output;
        _processHost = processHost;
        _events = events;
        _sdk = sdk;
        _notifications = notifications;
        _launchSettingsService = launchSettingsService;

        Configurations = ["Release", "Debug"];
        Frameworks = ["Project default", "net10.0", "net9.0", "net8.0"];
        RuntimeIdentifiers = new ObservableCollection<string>(new[]
        {
            GetHostRuntimeIdentifier(),
            "Portable",
            "win-x64",
            "osx-arm64",
            "osx-x64",
            "linux-x64",
            "linux-arm64",
        }.Distinct(StringComparer.OrdinalIgnoreCase));
        SelectedConfiguration = Configurations[0];
        SelectedFramework = Frameworks[0];
        SelectedRuntimeIdentifier = RuntimeIdentifiers[0];
    }

    public ObservableCollection<ProjectOption> Projects { get; } = [];

    public ObservableCollection<string> Configurations { get; }

    public ObservableCollection<string> Frameworks { get; }

    public ObservableCollection<string> RuntimeIdentifiers { get; }

    [ObservableProperty]
    private ProjectOption? _selectedProject;

    [ObservableProperty]
    private string _selectedConfiguration = string.Empty;

    [ObservableProperty]
    private string _selectedFramework = string.Empty;

    [ObservableProperty]
    private string _selectedRuntimeIdentifier = string.Empty;

    [ObservableProperty]
    private bool _selfContained;

    [ObservableProperty]
    private bool _singleFile;

    [ObservableProperty]
    private bool _readyToRun;

    [ObservableProperty]
    private bool _trimmed;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private bool _saveProfile = true;

    [ObservableProperty]
    private string _profileName = "FolderProfile";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _status = "Choose a project and publish options.";

    partial void OnSelectedProjectChanged(ProjectOption? value)
    {
        if (value is null)
            return;

        var projectDirectory = Path.GetDirectoryName(value.Path) ?? string.Empty;
        ResetPublishDefaults(value.Path, projectDirectory);
        LoadPublishProfile(value.Path);
    }

    public void Load(string? requestedProjectPath = null)
    {
        Projects.Clear();

        foreach (var project in DiscoverProjects())
        {
            Projects.Add(new ProjectOption(Path.GetFileNameWithoutExtension(project), project));
        }

        var projectPaths = Projects.Select(p => p.Path).ToArray();
        var preferredProjectPath = ResolvePreferredProjectPath(requestedProjectPath, projectPaths);
        SelectedProject = Projects.FirstOrDefault(p => string.Equals(p.Path, preferredProjectPath, StringComparison.OrdinalIgnoreCase))
                          ?? Projects.FirstOrDefault();

        if (Projects.Count == 0)
            Status = "No C# project was found in the current workspace.";
        else if (!Status.StartsWith("Loaded publish profile", StringComparison.OrdinalIgnoreCase))
            Status = "Choose a project and publish options.";
    }

    [RelayCommand]
    private async Task BrowseOutputAsync(CancellationToken cancellationToken)
    {
        var folder = await _dialogs.PickFolderAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(folder))
            OutputPath = folder;
    }

    [RelayCommand]
    private void OpenSdkSetup()
    {
        _events.Publish(new DotnetSdkSetupRequestedEvent());
    }

    [RelayCommand]
    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        if (!Validate())
            return;

        var sdkStatus = await _sdk.GetStatusAsync(cancellationToken);
        if (!sdkStatus.IsDotnetAvailable || sdkStatus.InstalledSdks.Count == 0)
        {
            _events.Publish(new DotnetSdkSetupRequestedEvent());
            Status = ".NET SDK is required before publishing.";
            return;
        }

        IsRunning = true;
        Status = "Publishing...";

        try
        {
            Directory.CreateDirectory(OutputPath);

            if (SaveProfile)
                SavePublishProfile();

            _events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
            await RunDotnetAsync(BuildPublishArguments(), Path.GetDirectoryName(SelectedProject!.Path)!, cancellationToken);

            Status = "Publish completed.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "Unable to publish.";
            _notifications.ShowError("Unable to publish", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool Validate()
    {
        if (SelectedProject is null || !File.Exists(SelectedProject.Path))
        {
            Status = "Select a valid project.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            Status = "Choose an output folder.";
            return false;
        }

        if (SaveProfile && string.IsNullOrWhiteSpace(ProfileName))
        {
            Status = "Enter a publish profile name.";
            return false;
        }

        return true;
    }

    private IReadOnlyList<string> BuildPublishArguments()
    {
        var arguments = new List<string>
        {
            "publish",
            SelectedProject!.Path,
            "-c",
            SelectedConfiguration,
            "-o",
            OutputPath,
        };

        if (!IsDefaultFramework(SelectedFramework))
        {
            arguments.Add("-f");
            arguments.Add(SelectedFramework);
        }

        if (!IsPortableRuntime(SelectedRuntimeIdentifier))
        {
            arguments.Add("-r");
            arguments.Add(SelectedRuntimeIdentifier);
        }

        arguments.Add("--self-contained");
        arguments.Add(SelfContained.ToString().ToLowerInvariant());

        if (SingleFile)
            arguments.Add("/p:PublishSingleFile=true");
        if (ReadyToRun)
            arguments.Add("/p:PublishReadyToRun=true");
        if (Trimmed)
            arguments.Add("/p:PublishTrimmed=true");

        return arguments;
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

    private void SavePublishProfile()
    {
        var projectDirectory = Path.GetDirectoryName(SelectedProject!.Path)!;
        var profilesDirectory = Path.Combine(projectDirectory, "Properties", "PublishProfiles");
        Directory.CreateDirectory(profilesDirectory);

        var profilePath = Path.Combine(profilesDirectory, $"{SanitizeProfileName(ProfileName)}.pubxml");
        var project = new XElement("Project",
            new XAttribute("ToolsVersion", "4.0"),
            new XElement("PropertyGroup",
                new XElement("PublishProtocol", "FileSystem"),
                new XElement("Configuration", SelectedConfiguration),
                new XElement("PublishDir", OutputPath),
                new XElement("PublishUrl", OutputPath),
                new XElement("SelfContained", SelfContained),
                new XElement("PublishSingleFile", SingleFile),
                new XElement("PublishReadyToRun", ReadyToRun),
                new XElement("PublishTrimmed", Trimmed)));

        if (!IsDefaultFramework(SelectedFramework))
            project.Element("PropertyGroup")!.Add(new XElement("TargetFramework", SelectedFramework));
        if (!IsPortableRuntime(SelectedRuntimeIdentifier))
            project.Element("PropertyGroup")!.Add(new XElement("RuntimeIdentifier", SelectedRuntimeIdentifier));

        new XDocument(project).Save(profilePath);
    }

    private string? ResolvePreferredProjectPath(string? requestedProjectPath, string[] projectPaths)
    {
        if (!string.IsNullOrWhiteSpace(requestedProjectPath) && File.Exists(requestedProjectPath))
            return requestedProjectPath;

        if (!string.IsNullOrWhiteSpace(_workspace.Current.StartupProjectPath) &&
            File.Exists(_workspace.Current.StartupProjectPath))
            return _workspace.Current.StartupProjectPath;

        var launchSettingsProject = ResolveLaunchSettingsStartupProject();
        if (!string.IsNullOrWhiteSpace(launchSettingsProject))
            return launchSettingsProject;

        return projectPaths.FirstOrDefault(path =>
                   DotnetProjectRunCapability.CanRun(path) &&
                   Path.GetFileNameWithoutExtension(path).Contains("Desktop", StringComparison.OrdinalIgnoreCase))
               ?? projectPaths.FirstOrDefault(DotnetProjectRunCapability.CanRun)
               ?? projectPaths.FirstOrDefault();
    }

    private string? ResolveLaunchSettingsStartupProject()
    {
        var workspaceRoot = _launchSettingsService.GetWorkspaceRoot(_workspace.Current);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return null;

        var settings = _launchSettingsService.LoadAsync(workspaceRoot).GetAwaiter().GetResult();
        if (settings is null || string.IsNullOrWhiteSpace(settings.StartupProject))
            return null;

        var projectPath = Path.IsPathRooted(settings.StartupProject)
            ? settings.StartupProject
            : Path.GetFullPath(Path.Combine(workspaceRoot, settings.StartupProject));

        if (!File.Exists(projectPath))
            return null;

        _workspace.SetStartupProject(projectPath);
        return projectPath;
    }

    private void LoadPublishProfile(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return;

        var profilesDirectory = Path.Combine(projectDirectory, "Properties", "PublishProfiles");
        if (!Directory.Exists(profilesDirectory))
            return;

        var profilePath = Directory
            .EnumerateFiles(profilesDirectory, "*.pubxml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (profilePath is null)
            return;

        try
        {
            var document = XDocument.Load(profilePath);
            ProfileName = Path.GetFileNameWithoutExtension(profilePath);
            SaveProfile = true;

            SelectedConfiguration = ResolveOption(Configurations, ReadProperty(document, "Configuration"), SelectedConfiguration);
            SelectedFramework = ResolveOption(Frameworks, ReadProperty(document, "TargetFramework"), "Project default");
            SelectedRuntimeIdentifier = ResolveOption(RuntimeIdentifiers, ReadProperty(document, "RuntimeIdentifier"), SelectedRuntimeIdentifier);

            var publishDir = ReadProperty(document, "PublishDir")
                             ?? ReadProperty(document, "PublishUrl");
            if (!string.IsNullOrWhiteSpace(publishDir))
            {
                OutputPath = Path.IsPathRooted(publishDir)
                    ? publishDir
                    : Path.GetFullPath(Path.Combine(projectDirectory, publishDir));
            }

            SelfContained = ReadBool(document, "SelfContained", SelfContained);
            SingleFile = ReadBool(document, "PublishSingleFile", SingleFile);
            ReadyToRun = ReadBool(document, "PublishReadyToRun", ReadyToRun);
            Trimmed = ReadBool(document, "PublishTrimmed", Trimmed);
            Status = $"Loaded publish profile {ProfileName}.";
        }
        catch (Exception ex)
        {
            Status = $"Unable to load publish profile: {ex.Message}";
        }
    }

    private void ResetPublishDefaults(string projectPath, string projectDirectory)
    {
        SelectedConfiguration = Configurations[0];
        SelectedFramework = "Project default";
        SelectedRuntimeIdentifier = RuntimeIdentifiers[0];
        SelfContained = false;
        SingleFile = false;
        ReadyToRun = false;
        Trimmed = false;
        SaveProfile = true;
        OutputPath = Path.Combine(projectDirectory, "bin", SelectedConfiguration, "publish");
        ProfileName = $"{SanitizeProfileName(Path.GetFileNameWithoutExtension(projectPath))}-Folder";
        Status = "Choose a project and publish options.";
    }

    private static string? ReadProperty(XDocument document, string name)
    {
        return document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == name)
            ?.Value
            .Trim();
    }

    private static bool ReadBool(XDocument document, string name, bool fallback)
    {
        var value = ReadProperty(document, name);
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string ResolveOption(ObservableCollection<string> options, string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var existing = options.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        options.Add(value);
        return value;
    }

    private string[] DiscoverProjects()
    {
        var roots = new[]
        {
            Path.GetDirectoryName(_workspace.Current.CurrentSolutionPath),
            _workspace.Current.CurrentFolderPath,
            Path.GetDirectoryName(_workspace.Current.CurrentFilePath),
        };

        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(root => Directory.EnumerateFiles(root!, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string SanitizeProfileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "FolderProfile" : sanitized;
    }

    private static bool IsDefaultFramework(string value) =>
        string.Equals(value, "Project default", StringComparison.OrdinalIgnoreCase);

    private static bool IsPortableRuntime(string value) =>
        string.Equals(value, "Portable", StringComparison.OrdinalIgnoreCase);

    private static string GetHostRuntimeIdentifier()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            _ => "x64",
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"win-{architecture}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"osx-{architecture}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"linux-{architecture}";

        return "Portable";
    }
}
