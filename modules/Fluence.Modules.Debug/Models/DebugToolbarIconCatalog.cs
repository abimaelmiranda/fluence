using Avalonia.Media;

namespace Fluence.Modules.Debug.Models;

public static class DebugToolbarIconCatalog
{
    public static readonly StreamGeometry ContinueIcon = StreamGeometry.Parse("M3 2.5 L12.5 8 L3 13.5 Z");
    public static readonly StreamGeometry StepOverIcon = StreamGeometry.Parse("M3 2.5 L9 8 L3 13.5 Z M11 2.5 L12.5 2.5 L12.5 13.5 L11 13.5 Z");
    public static readonly StreamGeometry StepIntoIcon = StreamGeometry.Parse("M6 2.5 L9 2.5 L9 8 L12 8 L7.5 12.5 L3 8 L6 8 Z M3 13 L12 13 L12 14.5 L3 14.5 Z");
    public static readonly StreamGeometry StepOutIcon = StreamGeometry.Parse("M3 1.5 L12 1.5 L12 3 L3 3 Z M7.5 4.5 L12 9 L9 9 L9 14.5 L6 14.5 L6 9 L3 9 Z");
    public static readonly StreamGeometry StopIcon = StreamGeometry.Parse("M3 3 L12 3 L12 12 L3 12 Z");
    public static readonly StreamGeometry ReloadIcon = StreamGeometry.Parse("M6.2 4.2 L3.5 4.2 L3.5 1.5 L5 1.5 L5 2.6 A7 7 0 0 1 13.2 4.2 L11.8 5.6 A5.2 5.2 0 0 0 5.8 4.5 L7.4 6.1 L2.8 6.1 L2.8 1.5 L4.2 2.9 A8.4 8.4 0 0 1 14.4 5.2 A8.4 8.4 0 0 1 12.8 14.4 A8.4 8.4 0 0 1 2.6 12.8 L3.9 11.5 A6.6 6.6 0 0 0 12.7 13.1 A6.6 6.6 0 0 0 13.1 5.3 A6.6 6.6 0 0 0 6.2 4.2 Z");
}
