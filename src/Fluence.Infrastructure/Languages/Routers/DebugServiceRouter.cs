using System;
using System.Collections.Generic;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Debugging;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Infrastructure.Languages.Routers;

public sealed class DebugServiceRouter(
    IServiceProvider serviceProvider,
    IWorkspaceContext workspace,
    ILanguageProfileRegistry profiles) : IDebugService
{
    private string ActiveKey => profiles.DetectWorkspaceLanguage(workspace) ?? "csharp";

    private IDebugService Active =>
        serviceProvider.GetRequiredKeyedService<IDebugService>(ActiveKey);

    public Task StartAsync(CancellationToken cancellationToken = default) => Active.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) => Active.StopAsync(cancellationToken);

    public Task RestartAsync(CancellationToken cancellationToken = default) => Active.RestartAsync(cancellationToken);

    public Task ContinueAsync(CancellationToken cancellationToken = default) => Active.ContinueAsync(cancellationToken);

    public Task StepOverAsync(CancellationToken cancellationToken = default) => Active.StepOverAsync(cancellationToken);

    public Task StepIntoAsync(CancellationToken cancellationToken = default) => Active.StepIntoAsync(cancellationToken);

    public Task StepOutAsync(CancellationToken cancellationToken = default) => Active.StepOutAsync(cancellationToken);

    public Task ToggleBreakpointAsync(string filePath, int line, CancellationToken cancellationToken = default) =>
        Active.ToggleBreakpointAsync(filePath, line, cancellationToken);

    public Task<DebugVariable?> EvaluateAsync(string expression, CancellationToken cancellationToken = default) =>
        Active.EvaluateAsync(expression, cancellationToken);

    public Task<IReadOnlyList<DebugVariable>> GetChildVariablesAsync(int variablesReference, CancellationToken cancellationToken = default) =>
        Active.GetChildVariablesAsync(variablesReference, cancellationToken);
}
