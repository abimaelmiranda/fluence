using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace Fluence.Modules.Terminal.Terminal;

public sealed class TerminalBuffer
{
    private const int MaxScrollbackLines = 5000;

    private readonly object _lock = new();
    private readonly List<TerminalCharacter[]> _scrollback = [];
    private TerminalCharacter[,] _cells;
    private int _cursorX;
    private int _cursorY;
    private int _savedCursorX;
    private int _savedCursorY;

    private Color _fg = Color.Parse("#F2F5F8");
    private Color _bg = Colors.Transparent;
    private bool _bold;

    private enum ParseState { Normal, Escape, Csi, Osc, OscEscape }
    private ParseState _state = ParseState.Normal;
    private string _csiParams = string.Empty;

    public TerminalBuffer(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        _cells = new TerminalCharacter[rows, columns];
        Clear();
    }

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int CursorX => _cursorX;
    public int CursorY => _cursorY;

    public event EventHandler? Updated;

    public TerminalCharacter Get(int row, int col) { lock (_lock) return _cells[row, col]; }

    public TerminalSnapshot TakeSnapshot()
    {
        lock (_lock)
        {
            var copy = new TerminalCharacter[Rows, Columns];
            Array.Copy(_cells, copy, _cells.Length);
            return new TerminalSnapshot(copy, Rows, Columns, _cursorX, _cursorY);
        }
    }

    public void Resize(int columns, int rows)
    {
        lock (_lock)
        {
            var newCells = new TerminalCharacter[rows, columns];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                    newCells[r, c] = TerminalCharacter.Empty;

            var rowsToCopy = Math.Min(Rows, rows);
            var columnsToCopy = Math.Min(Columns, columns);
            var oldStartRow = GetResizeSourceStartRow(rowsToCopy);
            var newStartRow = GetResizeTargetStartRow(rowsToCopy, rows, oldStartRow);

            for (int r = 0; r < rowsToCopy; r++)
                for (int c = 0; c < columnsToCopy; c++)
                    newCells[newStartRow + r, c] = _cells[oldStartRow + r, c];

            Columns = columns;
            Rows = rows;
            _cells = newCells;
            _cursorX = Math.Min(_cursorX, Columns - 1);
            _cursorY = Math.Clamp(_cursorY - oldStartRow + newStartRow, 0, Rows - 1);
            _savedCursorX = Math.Min(_savedCursorX, Columns - 1);
            _savedCursorY = Math.Clamp(_savedCursorY - oldStartRow + newStartRow, 0, Rows - 1);
        }
    }

    public void Write(string raw)
    {
        lock (_lock)
        {
            foreach (char ch in raw)
                ProcessChar(ch);
        }
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private void ProcessChar(char ch)
    {
        switch (_state)
        {
            case ParseState.Normal:
                if (ch == '\x1B') { _state = ParseState.Escape; return; }
                HandlePrintable(ch);
                break;

            case ParseState.Escape:
                switch (ch)
                {
                    case '[': _state = ParseState.Csi; _csiParams = string.Empty; break;
                    case ']': _state = ParseState.Osc; break;
                    case '7': SaveCursor(); _state = ParseState.Normal; break;
                    case '8': RestoreCursor(); _state = ParseState.Normal; break;
                    case 'c': Reset(); _state = ParseState.Normal; break;
                    default: _state = ParseState.Normal; break;
                }
                break;

            case ParseState.Csi:
                if (ch >= 0x40 && ch <= 0x7E)
                {
                    HandleCsi(_csiParams, ch);
                    _state = ParseState.Normal;
                    _csiParams = string.Empty;
                }
                else
                {
                    _csiParams += ch;
                }
                break;

            case ParseState.Osc:
                if (ch == '\a') _state = ParseState.Normal;
                else if (ch == '\x1B') _state = ParseState.OscEscape;
                break;

            case ParseState.OscEscape:
                _state = ch == '\\' ? ParseState.Normal : ParseState.Osc;
                break;
        }
    }

    private void HandlePrintable(char ch)
    {
        switch (ch)
        {
            case '\r': _cursorX = 0; break;
            case '\n': _cursorY++; if (_cursorY >= Rows) ScrollUp(); break;
            case '\b': if (_cursorX > 0) _cursorX--; break;
            case '\t': _cursorX = (_cursorX + 8) & ~7; if (_cursorX >= Columns) _cursorX = Columns - 1; break;
            default:
                if (ch >= 0x20)
                {
                    if (_cursorX >= Columns) { _cursorX = 0; _cursorY++; if (_cursorY >= Rows) ScrollUp(); }
                    _cells[_cursorY, _cursorX] = new TerminalCharacter { Glyph = ch, Foreground = _fg, Background = _bg, Bold = _bold };
                    _cursorX++;
                }
                break;
        }
    }

    private void HandleCsi(string p, char cmd)
    {
        bool privateMode = p.StartsWith("?", StringComparison.Ordinal);
        p = NormalizeCsiParams(p);
        var parts = p.Split(';');
        int P(int i, int def = 1) => parts.Length > i && int.TryParse(parts[i], out int v) ? v : def;

        if (privateMode && (cmd is 'h' or 'l')) return;

        switch (cmd)
        {
            case 'A': _cursorY = Math.Max(0, _cursorY - P(0)); break;
            case 'B': _cursorY = Math.Min(Rows - 1, _cursorY + P(0)); break;
            case 'C': _cursorX = Math.Min(Columns - 1, _cursorX + P(0)); break;
            case 'D': _cursorX = Math.Max(0, _cursorX - P(0)); break;
            case 'G': _cursorX = Math.Clamp(P(0, 1) - 1, 0, Columns - 1); break;
            case 'd': _cursorY = Math.Clamp(P(0, 1) - 1, 0, Rows - 1); break;
            case 'H': case 'f':
                _cursorY = Math.Clamp(P(0, 1) - 1, 0, Rows - 1);
                _cursorX = Math.Clamp(P(1, 1) - 1, 0, Columns - 1);
                break;
            case 'J': ClearScreen(P(0, 0)); break;
            case 'K': ClearLine(P(0, 0)); break;
            case 'm': ApplySgr(parts); break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
        }
    }

    private static string NormalizeCsiParams(string value)
    {
        int start = 0;
        while (start < value.Length && (value[start] == '?' || value[start] == '>' || value[start] == '='))
            start++;
        return start == 0 ? value : value[start..];
    }

    private void ClearScreen(int mode)
    {
        if (mode == 3) _scrollback.Clear();
    }

    private void ClearLine(int mode)
    {
        int start = mode == 0 ? _cursorX : 0;
        int end = mode == 1 ? _cursorX + 1 : Columns;
        for (int c = start; c < end; c++) _cells[_cursorY, c] = TerminalCharacter.Empty;
    }

    private void ApplySgr(string[] parts)
    {
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int code)) continue;

            switch (code)
            {
                case 0: _fg = Color.Parse("#F2F5F8"); _bg = Colors.Transparent; _bold = false; break;
                case 1: _bold = true; break;
                case 22: _bold = false; break;
                case >= 30 and <= 37: _fg = Ansi16[code - 30]; break;
                case >= 90 and <= 97: _fg = Ansi16[code - 90 + 8]; break;
                case >= 40 and <= 47: _bg = Ansi16[code - 40]; break;
                case >= 100 and <= 107: _bg = Ansi16[code - 100 + 8]; break;
                case 38 when i + 2 < parts.Length && parts[i + 1] == "5":
                    _fg = Ansi256(int.Parse(parts[i + 2])); i += 2; break;
                case 48 when i + 2 < parts.Length && parts[i + 1] == "5":
                    _bg = Ansi256(int.Parse(parts[i + 2])); i += 2; break;
                case 38 when i + 4 < parts.Length && parts[i + 1] == "2":
                    _fg = Color.FromRgb(byte.Parse(parts[i + 2]), byte.Parse(parts[i + 3]), byte.Parse(parts[i + 4])); i += 4; break;
                case 48 when i + 4 < parts.Length && parts[i + 1] == "2":
                    _bg = Color.FromRgb(byte.Parse(parts[i + 2]), byte.Parse(parts[i + 3]), byte.Parse(parts[i + 4])); i += 4; break;
                case 39: _fg = Color.Parse("#F2F5F8"); break;
                case 49: _bg = Colors.Transparent; break;
            }
        }
    }

    private void ScrollUp()
    {
        AppendLineToScrollback(0);
        for (int r = 0; r < Rows - 1; r++)
            for (int c = 0; c < Columns; c++)
                _cells[r, c] = _cells[r + 1, c];
        for (int c = 0; c < Columns; c++) _cells[Rows - 1, c] = TerminalCharacter.Empty;
        _cursorY = Rows - 1;
    }

    private void Clear()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                _cells[r, c] = TerminalCharacter.Empty;
        _cursorX = 0;
        _cursorY = 0;
    }

    private void Reset()
    {
        _scrollback.Clear();
        _fg = Color.Parse("#F2F5F8");
        _bg = Colors.Transparent;
        _bold = false;
        _state = ParseState.Normal;
        _csiParams = string.Empty;
        Clear();
    }

    private void SaveCursor() { _savedCursorX = _cursorX; _savedCursorY = _cursorY; }

    private void RestoreCursor()
    {
        _cursorX = Math.Clamp(_savedCursorX, 0, Columns - 1);
        _cursorY = Math.Clamp(_savedCursorY, 0, Rows - 1);
    }

    private int GetResizeSourceStartRow(int rowsToCopy)
    {
        if (rowsToCopy >= Rows) return 0;
        var first = FirstNonEmptyRow();
        var last = LastNonEmptyRow();
        if (first < 0 || last < 0) return Math.Max(0, Rows - rowsToCopy);
        if (last < rowsToCopy && _cursorY < rowsToCopy) return 0;
        return Math.Max(0, Rows - rowsToCopy);
    }

    private int GetResizeTargetStartRow(int rowsToCopy, int newRows, int oldStartRow)
    {
        if (newRows <= Rows) return 0;
        return oldStartRow == 0 ? 0 : Math.Max(0, newRows - rowsToCopy);
    }

    private int FirstNonEmptyRow()
    {
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Columns; col++)
                if (!IsEmpty(_cells[row, col])) return row;
        return -1;
    }

    private int LastNonEmptyRow()
    {
        for (int row = Rows - 1; row >= 0; row--)
            for (int col = 0; col < Columns; col++)
                if (!IsEmpty(_cells[row, col])) return row;
        return -1;
    }

    private static bool IsEmpty(TerminalCharacter ch) =>
        (ch.Glyph is ' ' or '\0') && ch.Background == Colors.Transparent;

    private void AppendLineToScrollback(int row)
    {
        var line = new TerminalCharacter[Columns];
        for (int col = 0; col < Columns; col++)
            line[col] = _cells[row, col];
        _scrollback.Add(line);
        if (_scrollback.Count > MaxScrollbackLines)
            _scrollback.RemoveAt(0);
    }

    private static readonly Color[] Ansi16 =
    [
        Color.FromRgb(0,   0,   0),
        Color.FromRgb(170, 0,   0),
        Color.FromRgb(0,   170, 0),
        Color.FromRgb(170, 170, 0),
        Color.FromRgb(0,   0,   170),
        Color.FromRgb(170, 0,   170),
        Color.FromRgb(0,   170, 170),
        Color.FromRgb(170, 170, 170),
        Color.FromRgb(85,  85,  85),
        Color.FromRgb(255, 85,  85),
        Color.FromRgb(85,  255, 85),
        Color.FromRgb(255, 255, 85),
        Color.FromRgb(85,  85,  255),
        Color.FromRgb(255, 85,  255),
        Color.FromRgb(85,  255, 255),
        Color.FromRgb(255, 255, 255),
    ];

    private static Color Ansi256(int idx)
    {
        if (idx < 16) return Ansi16[idx];
        if (idx >= 232) { byte v = (byte)(8 + (idx - 232) * 10); return Color.FromRgb(v, v, v); }
        idx -= 16;
        int r = idx / 36, g = (idx % 36) / 6, b = idx % 6;
        byte Cv(int v) => v == 0 ? (byte)0 : (byte)(55 + v * 40);
        return Color.FromRgb(Cv(r), Cv(g), Cv(b));
    }
}

public readonly struct TerminalSnapshot(
    TerminalCharacter[,] cells,
    int rows,
    int columns,
    int cursorX,
    int cursorY)
{
    public TerminalCharacter[,] Cells { get; } = cells;
    public int Rows { get; } = rows;
    public int Columns { get; } = columns;
    public int CursorX { get; } = cursorX;
    public int CursorY { get; } = cursorY;
}
