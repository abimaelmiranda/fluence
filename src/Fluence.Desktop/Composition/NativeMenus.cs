using Avalonia.Controls;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Models.Keybindings;
using Fluence.Desktop.ViewModels;

namespace Fluence.Desktop.Composition;

internal static class NativeMenus
{
    public static NativeMenu CreateMainMenu(MainWindowViewModel mainWindow, IKeybindingService keybindings)
    {
        return new NativeMenu
        {
            Items =
            {
                CreateFileMenu(mainWindow, keybindings),
                CreateMenu("Edit", "Undo", "Redo"),
                CreateViewMenu(mainWindow, keybindings),
                CreateToolsMenu(mainWindow, keybindings),
                CreateRunMenu(mainWindow, keybindings),
                CreateDebugMenu(mainWindow),
            },
        };
    }

    private static NativeMenuItem CreateFileMenu(MainWindowViewModel mainWindow, IKeybindingService keybindings)
    {
        var welcome = mainWindow.Welcome;

        return new NativeMenuItem
        {
            Header = "File",
            Menu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem
                    {
                        Header = "New Project",
                        Command = mainWindow.NewProjectCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Open File",
                        Command = welcome.OpenFileCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Open Folder",
                        Command = welcome.OpenFolderCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Open Solution",
                        Command = welcome.OpenSolutionCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = WithGesture("Save", keybindings.GetGesture(CommandIds.SaveActiveDocument)),
                        Command = mainWindow.SaveActiveDocumentCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Preferences",
                        Menu = new NativeMenu
                        {
                            Items =
                            {
                                new NativeMenuItem
                                {
                                    Header = ".NET SDK",
                                    Command = mainWindow.OpenDotnetSdkSetupCommand,
                                },
                                new NativeMenuItem
                                {
                                    Header = WithGesture("Settings", keybindings.GetGesture(CommandIds.OpenSettings)),
                                    Command = mainWindow.OpenSettingsCommand,
                                },
                                new NativeMenuItem
                                {
                                    Header = WithGesture("Keyboard Shortcuts", keybindings.GetGesture(CommandIds.OpenKeybindings)),
                                    Command = mainWindow.OpenKeybindingsCommand,
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private static NativeMenuItem CreateRunMenu(MainWindowViewModel mainWindow, IKeybindingService keybindings)
    {
        return new NativeMenuItem
        {
            Header = "Run",
            Menu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem
                    {
                        Header = WithGesture("Build", keybindings.GetGesture(CommandIds.Build)),
                        Command = mainWindow.BuildCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Restore",
                        Command = mainWindow.RestoreCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = WithGesture("Publish", keybindings.GetGesture(CommandIds.Publish)),
                        Command = mainWindow.PublishProjectCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = WithGesture("Run", keybindings.GetGesture(CommandIds.Run)),
                        Command = mainWindow.RunCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = WithGesture("Debug", keybindings.GetGesture(CommandIds.Debug)),
                        Command = mainWindow.DebugCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = WithGesture("Stop Debugging", keybindings.GetGesture(CommandIds.StopDebug)),
                        Command = mainWindow.StopDebugCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Test",
                        Command = mainWindow.TestCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Clean",
                        Command = mainWindow.CleanCommand,
                    },
                },
            },
        };
    }

    private static NativeMenuItem CreateToolsMenu(MainWindowViewModel mainWindow, IKeybindingService keybindings)
    {
        return new NativeMenuItem
        {
            Header = "Tools",
            Menu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem
                    {
                        Header = WithGesture(".NET SDK", keybindings.GetGesture(CommandIds.OpenDotnetSdkSetup)),
                        Command = mainWindow.OpenDotnetSdkSetupCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = WithGesture("Publish", keybindings.GetGesture(CommandIds.Publish)),
                        Command = mainWindow.PublishProjectCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = WithGesture("NuGet Package Manager", keybindings.GetGesture(CommandIds.ManageNuGetPackages)),
                        Command = mainWindow.ManageNuGetPackagesCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = WithGesture("Settings", keybindings.GetGesture(CommandIds.OpenSettings)),
                        Command = mainWindow.OpenSettingsCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = WithGesture("Keyboard Shortcuts", keybindings.GetGesture(CommandIds.OpenKeybindings)),
                        Command = mainWindow.OpenKeybindingsCommand,
                    },
                },
            },
        };
    }

    private static NativeMenuItem CreateViewMenu(MainWindowViewModel mainWindow, IKeybindingService keybindings)
    {
        return new NativeMenuItem
        {
            Header = "View",
            Menu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem
                    {
                        Header = WithGesture("Toggle Bottom Bar", keybindings.GetGesture(CommandIds.ToggleTerminal)),
                        Command = mainWindow.ToggleTerminalCommand,
                    },
                },
            },
        };
    }

    private static string WithGesture(string label, string? gesture) =>
        string.IsNullOrWhiteSpace(gesture) ? label : $"{label} ({gesture})";

    private static NativeMenuItem CreateDebugMenu(MainWindowViewModel mainWindow)
    {
        return new NativeMenuItem
        {
            Header = "Debug",
            Menu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem
                    {
                        Header = "Simulate Solution Load Failure",
                        Command = mainWindow.SimulateSolutionLoadFailureCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Simulate Missing Project",
                        Command = mainWindow.SimulateMissingProjectCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Simulate Broken MSBuild Project",
                        Command = mainWindow.SimulateBrokenMsBuildProjectCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Simulate Unsupported File Open",
                        Command = mainWindow.SimulateUnsupportedFileOpenCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Simulate Deleted File Open",
                        Command = mainWindow.SimulateDeletedFileOpenCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Simulate Unauthorized File Open",
                        Command = mainWindow.SimulateUnauthorizedFileOpenCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Trigger Crash",
                        Command = mainWindow.TriggerCrashCommand,
                    },
                },
            },
        };
    }

    private static NativeMenuItem CreateMenu(string header, params string[] items)
    {
        var menu = new NativeMenu();

        foreach (var item in items)
        {
            menu.Items.Add(new NativeMenuItem { Header = item });
        }

        return new NativeMenuItem
        {
            Header = header,
            Menu = menu,
        };
    }
}
