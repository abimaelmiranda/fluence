using Avalonia.Controls;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Models.Keybindings;
using Fluence.Core.Services.Localization;
using Fluence.Desktop.ViewModels;

namespace Fluence.Desktop.Composition;

internal static class NativeMenus
{
    public static MainMenuHandle CreateMainMenu(MainWindowViewModel mainWindow, IKeybindingService keybindings) =>
        new(mainWindow, keybindings);

    public sealed class MainMenuHandle
    {
        private readonly NativeMenu _menu = new();

        private readonly NativeMenuItem _file = new();
        private readonly NativeMenuItem _edit = new();
        private readonly NativeMenuItem _view = new();
        private readonly NativeMenuItem _tools = new();
        private readonly NativeMenuItem _run = new();
        private readonly NativeMenuItem _debug = new();

        private readonly NativeMenuItem _newProject = new();
        private readonly NativeMenuItem _openFile = new();
        private readonly NativeMenuItem _openFolder = new();
        private readonly NativeMenuItem _openSolution = new();
        private readonly NativeMenuItem _save = new();
        private readonly NativeMenuItem _preferences = new();
        private readonly NativeMenuItem _fileDotnetSdk = new();
        private readonly NativeMenuItem _fileSettings = new();
        private readonly NativeMenuItem _fileKeybindings = new();
        private readonly NativeMenuItem _undo = new();
        private readonly NativeMenuItem _redo = new();
        private readonly NativeMenuItem _build = new();
        private readonly NativeMenuItem _restore = new();
        private readonly NativeMenuItem _publish = new();
        private readonly NativeMenuItem _runProject = new();
        private readonly NativeMenuItem _debugProject = new();
        private readonly NativeMenuItem _stopDebugging = new();
        private readonly NativeMenuItem _test = new();
        private readonly NativeMenuItem _clean = new();
        private readonly NativeMenuItem _toolsDotnetSdk = new();
        private readonly NativeMenuItem _toolsPublish = new();
        private readonly NativeMenuItem _toolsNugetPackageManager = new();
        private readonly NativeMenuItem _toolsSettings = new();
        private readonly NativeMenuItem _toolsKeybindings = new();
        private readonly NativeMenuItem _toggleBottomBar = new();
        private readonly NativeMenuItem _simulateSolutionLoadFailure = new();
        private readonly NativeMenuItem _simulateMissingProject = new();
        private readonly NativeMenuItem _simulateBrokenMsBuildProject = new();
        private readonly NativeMenuItem _simulateUnsupportedFileOpen = new();
        private readonly NativeMenuItem _simulateDeletedFileOpen = new();
        private readonly NativeMenuItem _simulateUnauthorizedFileOpen = new();
        private readonly NativeMenuItem _triggerCrash = new();

        private readonly string? _saveGesture;
        private readonly string? _settingsGesture;
        private readonly string? _keybindingsGesture;
        private readonly string? _buildGesture;
        private readonly string? _publishGesture;
        private readonly string? _runGesture;
        private readonly string? _debugGesture;
        private readonly string? _stopDebuggingGesture;
        private readonly string? _toggleBottomBarGesture;

        public MainMenuHandle(MainWindowViewModel mainWindow, IKeybindingService keybindings)
        {
            var welcome = mainWindow.Welcome;

            _saveGesture = keybindings.GetGesture(CommandIds.SaveActiveDocument);
            _settingsGesture = keybindings.GetGesture(CommandIds.OpenSettings);
            _keybindingsGesture = keybindings.GetGesture(CommandIds.OpenKeybindings);
            _buildGesture = keybindings.GetGesture(CommandIds.Build);
            _publishGesture = keybindings.GetGesture(CommandIds.Publish);
            _runGesture = keybindings.GetGesture(CommandIds.Run);
            _debugGesture = keybindings.GetGesture(CommandIds.Debug);
            _stopDebuggingGesture = keybindings.GetGesture(CommandIds.StopDebug);
            _toggleBottomBarGesture = keybindings.GetGesture(CommandIds.ToggleTerminal);

            _newProject.Command = mainWindow.NewProjectCommand;
            _openFile.Command = welcome.OpenFileCommand;
            _openFolder.Command = welcome.OpenFolderCommand;
            _openSolution.Command = welcome.OpenSolutionCommand;
            _save.Command = mainWindow.SaveActiveDocumentCommand;
            _fileDotnetSdk.Command = mainWindow.OpenDotnetSdkSetupCommand;
            _fileSettings.Command = mainWindow.OpenSettingsCommand;
            _fileKeybindings.Command = mainWindow.OpenKeybindingsCommand;
            _build.Command = mainWindow.BuildCommand;
            _restore.Command = mainWindow.RestoreCommand;
            _publish.Command = mainWindow.PublishProjectCommand;
            _runProject.Command = mainWindow.RunCommand;
            _debugProject.Command = mainWindow.DebugCommand;
            _stopDebugging.Command = mainWindow.StopDebugCommand;
            _test.Command = mainWindow.TestCommand;
            _clean.Command = mainWindow.CleanCommand;

            _toolsDotnetSdk.Command = mainWindow.OpenDotnetSdkSetupCommand;
            _toolsPublish.Command = mainWindow.PublishProjectCommand;
            _toolsNugetPackageManager.Command = mainWindow.ManageNuGetPackagesCommand;
            _toolsSettings.Command = mainWindow.OpenSettingsCommand;
            _toolsKeybindings.Command = mainWindow.OpenKeybindingsCommand;
            _toggleBottomBar.Command = mainWindow.ToggleTerminalCommand;

            _simulateSolutionLoadFailure.Command = mainWindow.SimulateSolutionLoadFailureCommand;
            _simulateMissingProject.Command = mainWindow.SimulateMissingProjectCommand;
            _simulateBrokenMsBuildProject.Command = mainWindow.SimulateBrokenMsBuildProjectCommand;
            _simulateUnsupportedFileOpen.Command = mainWindow.SimulateUnsupportedFileOpenCommand;
            _simulateDeletedFileOpen.Command = mainWindow.SimulateDeletedFileOpenCommand;
            _simulateUnauthorizedFileOpen.Command = mainWindow.SimulateUnauthorizedFileOpenCommand;
            _triggerCrash.Command = mainWindow.TriggerCrashCommand;

            _file.Menu = new NativeMenu
            {
                Items =
                {
                    _newProject,
                    _openFile,
                    _openFolder,
                    _openSolution,
                    _save,
                    _preferences,
                },
            };

            _preferences.Menu = new NativeMenu
            {
                Items =
                {
                    _fileDotnetSdk,
                    _fileSettings,
                    _fileKeybindings,
                },
            };

            _edit.Menu = new NativeMenu
            {
                Items =
                {
                    _undo,
                    _redo,
                },
            };

            _view.Menu = new NativeMenu
            {
                Items =
                {
                    _toggleBottomBar,
                },
            };

            _tools.Menu = new NativeMenu
            {
                Items =
                {
                    _toolsDotnetSdk,
                    _toolsPublish,
                    _toolsNugetPackageManager,
                    _toolsSettings,
                    _toolsKeybindings,
                },
            };

            _run.Menu = new NativeMenu
            {
                Items =
                {
                    _build,
                    _restore,
                    _publish,
                    _runProject,
                    _debugProject,
                    _stopDebugging,
                    _test,
                    _clean,
                },
            };

            _debug.Menu = new NativeMenu
            {
                Items =
                {
                    _simulateSolutionLoadFailure,
                    _simulateMissingProject,
                    _simulateBrokenMsBuildProject,
                    _simulateUnsupportedFileOpen,
                    _simulateDeletedFileOpen,
                    _simulateUnauthorizedFileOpen,
                    _triggerCrash,
                },
            };

            _menu.Items.Add(_file);
            _menu.Items.Add(_edit);
            _menu.Items.Add(_view);
            _menu.Items.Add(_tools);
            _menu.Items.Add(_run);
            _menu.Items.Add(_debug);

            Refresh();
        }

        public NativeMenu Menu => _menu;

        public void Refresh()
        {
            _file.Header = T("Desktop.Menu.File");
            _edit.Header = T("Desktop.Menu.Edit");
            _view.Header = T("Desktop.Menu.View");
            _tools.Header = T("Desktop.Menu.Tools");
            _run.Header = T("Desktop.Menu.Run");
            _debug.Header = T("Desktop.Menu.Debug");

            _newProject.Header = T("Desktop.Menu.NewProject");
            _openFile.Header = T("Desktop.Menu.OpenFile");
            _openFolder.Header = T("Desktop.Menu.OpenFolder");
            _openSolution.Header = T("Desktop.Menu.OpenSolution");
            _save.Header = WithGesture(T("Desktop.Menu.Save"), _saveGesture);
            _preferences.Header = T("Desktop.Menu.Preferences");
            _fileDotnetSdk.Header = T("Desktop.Menu.DotnetSdk");
            _fileSettings.Header = WithGesture(T("Desktop.Menu.Settings"), _settingsGesture);
            _fileKeybindings.Header = WithGesture(T("Desktop.Menu.KeyboardShortcuts"), _keybindingsGesture);

            _undo.Header = T("Desktop.Menu.Undo");
            _redo.Header = T("Desktop.Menu.Redo");

            _build.Header = WithGesture(T("Desktop.Menu.Build"), _buildGesture);
            _restore.Header = T("Desktop.Menu.Restore");
            _publish.Header = WithGesture(T("Desktop.Menu.Publish"), _publishGesture);
            _runProject.Header = WithGesture(T("Desktop.Menu.RunProject"), _runGesture);
            _debugProject.Header = WithGesture(T("Desktop.Menu.DebugProject"), _debugGesture);
            _stopDebugging.Header = WithGesture(T("Desktop.Menu.StopDebugging"), _stopDebuggingGesture);
            _test.Header = T("Desktop.Menu.Test");
            _clean.Header = T("Desktop.Menu.Clean");

            _toolsDotnetSdk.Header = T("Desktop.Menu.DotnetSdk");
            _toolsPublish.Header = WithGesture(T("Desktop.Menu.Publish"), _publishGesture);
            _toolsNugetPackageManager.Header = T("Desktop.Menu.NuGetPackageManager");
            _toolsSettings.Header = WithGesture(T("Desktop.Menu.Settings"), _settingsGesture);
            _toolsKeybindings.Header = WithGesture(T("Desktop.Menu.KeyboardShortcuts"), _keybindingsGesture);
            _toggleBottomBar.Header = WithGesture(T("Desktop.Menu.ToggleBottomBar"), _toggleBottomBarGesture);

            _simulateSolutionLoadFailure.Header = T("Desktop.Menu.SimulateSolutionLoadFailure");
            _simulateMissingProject.Header = T("Desktop.Menu.SimulateMissingProject");
            _simulateBrokenMsBuildProject.Header = T("Desktop.Menu.SimulateBrokenMsBuildProject");
            _simulateUnsupportedFileOpen.Header = T("Desktop.Menu.SimulateUnsupportedFileOpen");
            _simulateDeletedFileOpen.Header = T("Desktop.Menu.SimulateDeletedFileOpen");
            _simulateUnauthorizedFileOpen.Header = T("Desktop.Menu.SimulateUnauthorizedFileOpen");
            _triggerCrash.Header = T("Desktop.Menu.TriggerCrash");
        }
    }

    private static string WithGesture(string label, string? gesture) =>
        string.IsNullOrWhiteSpace(gesture) ? label : $"{label} ({gesture})";

    private static string T(string key) => Locale.Current[key];
}
