using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Fluence.Modules.Settings.ViewModels;

internal static class RoslynRuleCatalog
{
    public static ObservableCollection<SettingsStringArrayOption> KnownRules { get; } =
    [
        new("IDE0008", "Use explicit type", "Suppresses explicit type instead of var suggestions.", "Style"),
        new("IDE0160", "Use block scoped namespace", "Suppresses file scoped to block scoped namespace suggestions.", "Style"),
        new("IDE0058", "Expression value is never used", "Suppresses ignored expression value diagnostics.", "Style"),
        new("IDE0005", "Remove unnecessary using", "Suppresses unused using directive diagnostics.", "Style"),
        new("IDE0055", "Fix formatting", "Suppresses analyzer formatting diagnostics.", "Formatting"),
        new("CS8019", "Unnecessary using directive", "Suppresses compiler diagnostics for unused using directives.", "Compiler"),
    ];

    public static SettingsStringArrayOption? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return KnownRules.FirstOrDefault(rule =>
            string.Equals(rule.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
