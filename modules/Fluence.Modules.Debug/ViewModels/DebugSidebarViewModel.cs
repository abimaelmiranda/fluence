using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.Debug.ViewModels;

public sealed partial class DebugSidebarViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _sessionTitle = "No debug session";

    [ObservableProperty]
    private string _startupProject = "No project selected";

    [ObservableProperty]
    private string _architecture = "x64";

    public void Update(DebugSession session)
    {
        SessionTitle = session.IsActive ? "Debug session active" : "No debug session";
        StartupProject = session.StartupProjectId;
        Architecture = session.TargetArchitecture;
    }

    public void Clear()
    {
        SessionTitle = "No debug session";
        StartupProject = "No project selected";
        Architecture = "x64";
    }
}
