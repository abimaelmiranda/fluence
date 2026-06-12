using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Fluence.Core.Infrastructure;
using Fluence.Core.ViewModels;
using Fluence.Core.Workspace;
using XTerm.Events;
using XTerm.Options;
using XTerminal = global::XTerm.Terminal;

namespace Fluence.Modules.Terminal.ViewModels;

public sealed partial class TerminalViewModel : ViewModelBase, IDisposable
{
    private readonly ITerminalService _terminalService;
    private readonly IWorkspaceContext _workspace;
    private readonly XTerminal _xterm;

    public XTerminal XTerminal => _xterm;

    public event EventHandler? BufferRefreshed;

    public TerminalViewModel(ITerminalService terminalService, IWorkspaceContext workspace)
    {
        _terminalService = terminalService;
        _workspace = workspace;

        _xterm = new XTerminal(new TerminalOptions
        {
            Cols = 80,
            Rows = 24,
            Scrollback = 5000,
            TermName = "xterm-256color",
            ConvertEol = false,
        });

        _terminalService.DataReceived += OnDataReceived;
        _terminalService.Cleared += OnCleared;
    }

    public async Task StartShellAsync(string? workingDirectory = null)
    {
        await _terminalService.StartShellAsync(workingDirectory, _xterm.Cols, _xterm.Rows);
        _ = StartXtermBridgeAsync();
    }

    private async Task StartXtermBridgeAsync()
    {
        // Delay before wiring DataReceived so zsh's readline (zle) has time to initialize.
        // Without the delay, XTerm.NET response bytes (e.g. ESC[?1;2c) arrive at PTY stdin
        // before zle is ready and get interpreted as user input, corrupting zsh's startup.
        await Task.Delay(500);
        _xterm.DataReceived += OnXtermDataReceived;
    }

    private void OnXtermDataReceived(object? sender, TerminalEvents.DataEventArgs e)
    {
        _ = _terminalService.SendInputAsync(e.Data);
    }

    public async Task SendInputAsync(string text)
    {
        await _terminalService.SendInputAsync(text);
    }

    public async Task ResizeAsync(int cols, int rows)
    {
        _xterm.Resize(cols, rows);
        await _terminalService.ResizeAsync(cols, rows);
    }

    // Invoked by DotnetCliModule and others — keeps compatibility
    public async Task ExecuteAsync(string command)
    {
        await _terminalService.ExecuteAsync(command, GetWorkingDirectory());
    }

    private void OnDataReceived(object? sender, TerminalDataEventArgs e)
    {
        var text = Encoding.UTF8.GetString(e.Data);
        _xterm.Write(text);
        BufferRefreshed?.Invoke(this, EventArgs.Empty);
    }

    private void OnCleared(object? sender, EventArgs e)
    {
        _xterm.Clear();
    }

    public void WriteError(string message)
    {
        _xterm.Write($"\x1b[31m{message}\x1b[0m");
    }

    private string? GetWorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_workspace.Current.CurrentFolderPath))
            return _workspace.Current.CurrentFolderPath;

        var solutionPath = _workspace.Current.CurrentSolutionPath;
        return string.IsNullOrWhiteSpace(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
    }

    public void Dispose()
    {
        _terminalService.DataReceived -= OnDataReceived;
        _terminalService.Cleared -= OnCleared;
        _xterm.DataReceived -= OnXtermDataReceived;
        _xterm.Dispose();
    }
}
