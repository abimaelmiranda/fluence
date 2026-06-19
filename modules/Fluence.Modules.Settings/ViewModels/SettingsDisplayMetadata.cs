using System;
using System.Collections.Generic;
using Fluence.Core.Services.Localization;

namespace Fluence.Modules.Settings.ViewModels;

internal static class SettingsDisplayMetadata
{
    private static readonly Dictionary<string, string> SectionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["global"] = "Global",
        ["editor"] = "Editor",
        ["languageServer"] = "Language Server",
        ["shell"] = "Shell",
        ["problems"] = "Problems",
        ["debug"] = "Debug",
    };

    private static readonly Dictionary<string, string> PropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["global.FontFamily"] = "Global: Font Family",
        ["global.FontSize"] = "Global: Font Size",
        ["global.Theme"] = "Global: Theme",
        ["editor.FontFamily"] = "Editor: Font Family",
        ["editor.FontSize"] = "Editor: Font Size",
        ["editor.LineHeightFactor"] = "Editor: Line Height",
        ["editor.ShowLineNumbers"] = "Editor: Show Line Numbers",
        ["editor.AutoPairBrackets"] = "Editor: Auto Pair Brackets",
        ["editor.FormatOnSave"] = "Editor: Format On Save",
        ["editor.CompletionTriggerDelayMs"] = "Editor: Completion Trigger Delay",
        ["languageServer.SuppressedDiagnosticCodes"] = "Language Server: Suppressed Diagnostic Codes",
        ["languageServer.EnableMsBuild"] = "Language Server: Enable MSBuild",
        ["languageServer.LoadProjectsOnDemand"] = "Language Server: Load Projects On Demand",
        ["languageServer.EnablePackageAutoRestore"] = "Language Server: Enable Package Auto Restore",
        ["languageServer.EnableAnalyzersSupport"] = "Language Server: Enable Analyzers Support",
        ["languageServer.EnableDecompilationSupport"] = "Language Server: Enable Decompilation Support",
        ["languageServer.EnableImportCompletion"] = "Language Server: Enable Import Completion",
        ["languageServer.DiagnosticWorkersThreadCount"] = "Language Server: Diagnostic Workers Thread Count",
        ["languageServer.EnableEditorConfigSupport"] = "Language Server: Enable EditorConfig Support",
        ["languageServer.IncludePrereleases"] = "Language Server: Include Prereleases",
        ["shell.SidebarWidth"] = "Shell: Sidebar Width",
        ["shell.TerminalHeight"] = "Shell: Terminal Height",
        ["shell.AnimationsEnabled"] = "Shell: Animations Enabled",
        ["problems.ShowWarningsFromAllFiles"] = "Problems: Show Warnings From All Files",
        ["problems.ShowInformationFromAllFiles"] = "Problems: Show Information From All Files",
        ["problems.MaxVisibleProblems"] = "Problems: Max Visible Problems",
        ["debug.ExceptionBreakMode"] = "Debug: Exception Break Mode",
    };

    private static readonly Dictionary<string, string> PropertyDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["global.FontFamily"] = "Controls the default UI font family used by the IDE.",
        ["global.FontSize"] = "Controls the default UI font size in pixels.",
        ["global.Theme"] = "Selects the active application theme.",
        ["editor.FontFamily"] = "Controls the font family used in the code editor.",
        ["editor.FontSize"] = "Controls the editor font size in pixels.",
        ["editor.LineHeightFactor"] = "Controls editor line spacing as a multiplier of the font size.",
        ["editor.ShowLineNumbers"] = "Shows line numbers in editor gutters.",
        ["editor.AutoPairBrackets"] = "Automatically inserts the matching bracket while typing.",
        ["editor.FormatOnSave"] = "Formats files when they are saved, when a formatter is available.",
        ["editor.CompletionTriggerDelayMs"] = "Controls the delay before completion suggestions are requested.",
        ["languageServer.SuppressedDiagnosticCodes"] = "Suppresses Roslyn or OmniSharp diagnostic IDs and related code actions.",
        ["languageServer.EnableMsBuild"] = "Enables MSBuild project loading in OmniSharp.",
        ["languageServer.LoadProjectsOnDemand"] = "Loads projects only when they are needed by the language server.",
        ["languageServer.EnablePackageAutoRestore"] = "Allows OmniSharp to restore missing packages automatically.",
        ["languageServer.EnableAnalyzersSupport"] = "Enables analyzer diagnostics and code fixes from Roslyn analyzers.",
        ["languageServer.EnableDecompilationSupport"] = "Allows navigation into decompiled symbols when source is unavailable.",
        ["languageServer.EnableImportCompletion"] = "Includes import suggestions in completion results.",
        ["languageServer.DiagnosticWorkersThreadCount"] = "Controls how many workers OmniSharp uses for diagnostics.",
        ["languageServer.EnableEditorConfigSupport"] = "Applies .editorconfig formatting and analyzer preferences.",
        ["languageServer.IncludePrereleases"] = "Allows prerelease SDKs when resolving the language server SDK.",
        ["shell.SidebarWidth"] = "Controls the default width of the left sidebar.",
        ["shell.TerminalHeight"] = "Controls the default height of the bottom terminal panel.",
        ["shell.AnimationsEnabled"] = "Enables or disables shell transition animations.",
        ["problems.ShowWarningsFromAllFiles"] = "Shows warning diagnostics from files that are not currently open.",
        ["problems.ShowInformationFromAllFiles"] = "Shows informational diagnostics from files that are not currently open.",
        ["problems.MaxVisibleProblems"] = "Limits the number of problems shown in the Problems panel.",
        ["debug.ExceptionBreakMode"] = "Controls when the debugger stops on exceptions.",
    };

    public static string GetSectionDisplayName(string sectionName)
    {
        var fallback = SectionNames.TryGetValue(sectionName, out var displayName)
            ? displayName
            : SplitIdentifier(sectionName);
        return Localize($"Settings.Section.{sectionName}", fallback);
    }

    public static string GetPropertyDisplayName(string sectionName, string propertyName)
    {
        var key = $"{sectionName}.{propertyName}";
        var fallback = PropertyNames.TryGetValue(key, out var displayName)
            ? displayName
            : $"{GetSectionDisplayName(sectionName)}: {SplitIdentifier(propertyName)}";
        return Localize($"Settings.Property.{key}", fallback);
    }

    public static string GetPropertyDescription(string sectionName, string propertyName)
    {
        var key = $"{sectionName}.{propertyName}";
        var fallback = PropertyDescriptions.TryGetValue(key, out var description)
            ? description
            : $"Controls {SplitIdentifier(propertyName).ToLowerInvariant()}.";
        return Localize($"Settings.Description.{key}", fallback);
    }

    public static int GetSectionSortKey(string sectionName) =>
        sectionName switch
        {
            "global" => 0,
            "editor" => 1,
            "languageServer" => 2,
            "shell" => 3,
            "problems" => 4,
            "debug" => 5,
            _ => 100,
        };

    private static string SplitIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = new List<char>(value.Length + 8) { char.ToUpperInvariant(value[0]) };
        for (var i = 1; i < value.Length; i++)
        {
            var current = value[i];
            var previous = value[i - 1];
            if (char.IsUpper(current) && !char.IsWhiteSpace(previous) && !char.IsUpper(previous))
                chars.Add(' ');

            chars.Add(current);
        }

        return new string(chars.ToArray());
    }

    private static string Localize(string key, string fallback)
    {
        var value = Locale.Current[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
