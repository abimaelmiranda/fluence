using Avalonia.Controls;
using Fluence.Desktop.ViewModels;

namespace Fluence.Desktop.Composition;

internal static class NativeMenus
{
    public static NativeMenu CreateMainMenu(MainWindowViewModel mainWindow)
    {
        return new NativeMenu
        {
            Items =
            {
                CreateFileMenu(mainWindow),
                CreateMenu("Edit", "Undo", "Redo"),
                CreateViewMenu(mainWindow),
                CreateRunMenu(mainWindow),
                CreateDebugMenu(mainWindow),
            },
        };
    }

    private static NativeMenuItem CreateFileMenu(MainWindowViewModel mainWindow)
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
                        Header = "Save",
                        Command = mainWindow.SaveActiveDocumentCommand,
                    },
                },
            },
        };
    }

    private static NativeMenuItem CreateRunMenu(MainWindowViewModel mainWindow)
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
                        Header = "Build",
                        Command = mainWindow.BuildCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Restore",
                        Command = mainWindow.RestoreCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Run",
                        Command = mainWindow.RunCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Debug",
                        Command = mainWindow.DebugCommand,
                    },
                    new NativeMenuItem
                    {
                        Header = "Stop Debugging",
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

    private static NativeMenuItem CreateViewMenu(MainWindowViewModel mainWindow)
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
                        Header = "Toggle Terminal",
                        Command = mainWindow.ToggleTerminalCommand,
                    },
                },
            },
        };
    }

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
