using System;
using System.Collections.Generic;
using Fluence.Core.Models.Theming;

namespace Fluence.Core.Abstractions.Theming;

public interface IThemeLoader : IDisposable
{
    IdeTheme CurrentTheme { get; }

    IdeTheme Load(string? themeReference);

    IReadOnlyList<ThemeDescriptor> GetAvailableThemes();

    ThemeDescriptor InstallTheme(string sourcePath);

    IObservable<IdeTheme> Watch();
}
