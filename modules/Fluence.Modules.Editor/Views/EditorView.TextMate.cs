using System;
using System.IO;
using System.Text;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Themes;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView
{
    private void InitializeTextMate()
    {
        _registryOptions = new RegistryOptions(ThemeName.VisualStudioDark);
        _textMateInstallation = Editor.InstallTextMate(_registryOptions);

        var theme = LoadStandardTheme();
        if (theme is not null)
            _textMateInstallation.SetTheme(theme);

        // Must be registered AFTER TextMate so semantic colors override grammar colors.
        Editor.TextArea.TextView.LineTransformers.Add(_semanticColorizer);
    }

    private static IRawTheme? LoadStandardTheme()
    {
        var uri = new Uri("avares://Fluence.Modules.Editor/Assets/Themes/fluence-default-dark.json");

        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        return ThemeReader.ReadThemeSync(reader);
    }

    private void ApplyTextMateThemeJson(string themeJson)
    {
        if (_textMateInstallation is null || string.IsNullOrWhiteSpace(themeJson))
            return;

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(themeJson));
            using var reader = new StreamReader(stream);
            var theme = ThemeReader.ReadThemeSync(reader);
            _textMateInstallation.SetTheme(theme);
        }
        catch
        {
            var fallback = LoadStandardTheme();
            if (fallback is not null)
                _textMateInstallation.SetTheme(fallback);
        }
    }

    private void ApplyGrammarForPath(string? path)
    {
        if (_registryOptions is null || _textMateInstallation is null || string.IsNullOrEmpty(path))
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyGrammarForPath(path));
            return;
        }

        var scope = ResolveScopeName(path);
        if (!string.IsNullOrEmpty(scope))
            _textMateInstallation.SetGrammar(scope);
    }

    private string? ResolveScopeName(string path)
    {
        if (_registryOptions is null)
            return null;

        var extension = System.IO.Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
            return null;

        if (LanguageScopeByExtension.TryGetValue(extension, out var explicitScope))
            return explicitScope;

        var language = _registryOptions.GetLanguageByExtension(extension) ??
                       _registryOptions.GetLanguageByExtension(extension.TrimStart('.'));

        return language is null ? null : _registryOptions.GetScopeByLanguageId(language.Id);
    }
}
