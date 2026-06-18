namespace Fluence.Core.Models.LanguageServer;

public sealed record SemanticToken(int Line, int StartChar, int Length, string TokenType, string[] Modifiers);
