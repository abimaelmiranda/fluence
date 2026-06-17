namespace Fluence.Modules.DotnetCli.ViewModels;

public sealed record ProjectOption(string Name, string Path)
{
    public override string ToString() => Name;
}
