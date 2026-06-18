namespace Fluence.Modules.DotnetCli.ViewModels;

public sealed record DotnetTemplateOption(
    string Name,
    string ShortName,
    string Description,
    bool SupportsFramework = true)
{
    public override string ToString() => Name;
}
