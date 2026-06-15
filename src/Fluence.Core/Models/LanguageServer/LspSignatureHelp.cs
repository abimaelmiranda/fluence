using System.Collections.Generic;

namespace Fluence.Core.Models.LanguageServer;

public record LspParameterInformation(
    string Label,
    string? Documentation,
    int? LabelStart = null,
    int? LabelEnd = null);

public record LspSignatureInformation(
    string Label,
    string? Documentation,
    IReadOnlyList<LspParameterInformation>? Parameters);

public record LspSignatureHelp(
    IReadOnlyList<LspSignatureInformation> Signatures,
    int ActiveSignature,
    int ActiveParameter);
