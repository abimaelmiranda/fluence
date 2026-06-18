using System;
using System.IO;
using Fluence.Core.Abstractions.Infrastructure;
using Porta.Pty;

namespace Fluence.Infrastructure.Pty;

internal sealed class PortaPtySession : IPtySession
{
    private readonly IPtyConnection _pty;
    private volatile bool _hasExited;

    public PortaPtySession(IPtyConnection pty)
    {
        _pty = pty;
        _pty.ProcessExited += OnProcessExited;
    }

    public Stream Input => _pty.WriterStream;
    public Stream Output => _pty.ReaderStream;
    public bool HasExited => _hasExited;
    public event EventHandler? Exited;

    public void Resize(int columns, int rows) => _pty.Resize(columns, rows);

    private void OnProcessExited(object? sender, PtyExitedEventArgs e)
    {
        _hasExited = true;
        Exited?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _pty.ProcessExited -= OnProcessExited;
        try { _pty.Kill(); } catch { }
        _pty.Dispose();
    }
}
