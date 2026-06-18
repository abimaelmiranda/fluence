using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Models.Settings;

namespace Fluence.Modules.Debug;

[SettingsSection("debug")]
public sealed class DebugSettings
{
    public DebugExceptionBreakMode ExceptionBreakMode { get; set; } = DebugExceptionBreakMode.OnlyUserUnhandled;
}
