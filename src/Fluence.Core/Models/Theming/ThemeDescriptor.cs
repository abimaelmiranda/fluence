namespace Fluence.Core.Models.Theming;

public sealed record ThemeDescriptor(
    string Reference,
    string DisplayName,
    bool IsBuiltIn,
    string? Path = null);
