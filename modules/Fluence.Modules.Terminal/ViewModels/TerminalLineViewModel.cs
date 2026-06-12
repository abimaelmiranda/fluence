namespace Fluence.Modules.Terminal.ViewModels;

public sealed class TerminalLineViewModel(string text, bool isError)
{
    public string Text { get; } = text;
    public bool IsError { get; } = isError;
}
