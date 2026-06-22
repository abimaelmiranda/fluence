namespace Fluence.Core.Abstractions.Projects;

public sealed record ProjectTemplateDefinition(
    string Id,
    string Name,
    string Description,
    bool SupportsFramework = false)
{
    public override string ToString() => Name;
}
