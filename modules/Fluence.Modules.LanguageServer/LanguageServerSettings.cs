using Fluence.Core.Models.Settings;

namespace Fluence.Modules.LanguageServer;

[SettingsSection("languageServer")]
public sealed class LanguageServerSettings
{
    public string[] SuppressedDiagnosticCodes { get; set; } =
    [
        "IDE0008",
        "IDE0160",
        "IDE0058",
    ];

    public bool EnableMsBuild { get; set; } = true;

    public bool LoadProjectsOnDemand { get; set; } = false;

    public bool EnablePackageAutoRestore { get; set; } = true;

    public bool EnableAnalyzersSupport { get; set; } = true;

    public bool EnableDecompilationSupport { get; set; } = true;

    public bool EnableImportCompletion { get; set; } = true;

    public int DiagnosticWorkersThreadCount { get; set; } = 1;

    public bool EnableEditorConfigSupport { get; set; } = true;

    public bool IncludePrereleases { get; set; } = true;
}
