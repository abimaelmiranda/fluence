using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Input;
using Fluence.Core.Models.Keybindings;
using Fluence.Core.Abstractions.Modules;
using AvaloniaEdit.Document;

namespace Fluence.Modules.Workbench.Editor.Views;

public partial class EditorView
{
    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_viewModel?.ActiveDocumentPath is null)
            return;

        if (e.Text?.Length != 1)
            return;
        var ch = e.Text[0];

        // Signature help triggers
        if (ch == '(' || ch == ',')
            _ = TriggerSignatureHelpAsync(ch);
        else if (ch == ')')
            CloseSignatureHelpPopup();

        if (_completionService is null)
            return;
        if (ch != '.' && !char.IsLetter(ch) && ch != '_')
            return;

        // Dot while popup is open: close the existing generic popup and retrigger for member completion.
        if (ch == '.' && CompletionPopup.IsOpen)
            CloseCompletionPopup();

        if (CompletionPopup.IsOpen)
        {
            ScheduleCompletionWindowRefresh();
            return;
        }

        if (ch == '.')
            Interlocked.Exchange(ref _completionPostImmediate, 1);

        if (Interlocked.CompareExchange(ref _completionPostPending, 1, 0) != 0)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _completionPostPending, 0);
            var immediate = Interlocked.Exchange(ref _completionPostImmediate, 0) == 1;
            if (_completionService is null || _viewModel?.ActiveDocumentPath is null)
                return;

            if (CompletionPopup.IsOpen)
            {
                ScheduleCompletionWindowRefresh();
                return;
            }

            _ = TriggerCompletionAsync(immediate);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (e.Text?.Length != 1)
            return;

        var ch       = e.Text[0];
        var document = Editor.Document;
        if (document is null)
            return;

        var textArea    = Editor.TextArea;
        var selection   = textArea.Selection;
        var caretOffset = textArea.Caret.Offset;

        if (!AutoPairClosers.TryGetValue(ch, out var closer))
        {
            if (!selection.IsEmpty || !AutoPairClosingChars.Contains(ch))
                return;

            if (!_editorSettings.AutoPairBrackets)
                return;

            if (IsInsideComment(document, caretOffset))
                return;

            if (caretOffset < document.TextLength && document.GetCharAt(caretOffset) == ch)
            {
                SetCaretOffset(caretOffset + 1);
                if (ch == ')')
                    CloseSignatureHelpPopup();
                e.Handled = true;
            }

            return;
        }

        if (!_editorSettings.AutoPairBrackets || IsInsideComment(document, caretOffset))
            return;

        if (!selection.IsEmpty)
        {
            var selectionSegment = selection.SurroundingSegment;
            var selectedText     = document.GetText(selectionSegment.Offset, selectionSegment.Length);
            var wrappedText      = $"{ch}{selectedText}{closer}";
            document.Replace(selectionSegment.Offset, selectionSegment.Length, wrappedText);
            SetCaretOffset(selectionSegment.Offset + wrappedText.Length);
            e.Handled = true;
            return;
        }

        if (caretOffset < document.TextLength && document.GetCharAt(caretOffset) == closer)
        {
            SetCaretOffset(caretOffset + 1);
            e.Handled = true;
            return;
        }

        document.Insert(caretOffset, $"{ch}{closer}");
        SetCaretOffset(caretOffset + 1);
        if (ch == '(')
            _ = TriggerSignatureHelpAsync(ch);
        e.Handled = true;
    }

    private void OnEditorPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null) return;

        var gesture = EditorKeyGestureFormatter.FromEvent(e);
        if (MatchesEditorCommand(CommandIds.EditorQuickFix, gesture))
        {
            TriggerQuickFix();
            e.Handled = true;
            return;
        }

        if (e.Handled)
            return;

        if (TryHandleCodeActionPopupKey(e))
        {
            e.Handled = true;
            return;
        }

        if (TryHandleAcceleratedUndo(e))
        {
            e.Handled = true;
            return;
        }

        if (CompletionPopup.IsOpen)
        {
            if (e.Key == Key.Down)
            {
                var count = CompletionListBox.ItemCount;
                if (count > 0)
                    CompletionListBox.SelectedIndex = Math.Min(CompletionListBox.SelectedIndex + 1, count - 1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                if (CompletionListBox.SelectedIndex > 0)
                    CompletionListBox.SelectedIndex--;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter || (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None))
            {
                CommitCompletion();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                CloseCompletionPopup();
                e.Handled = true;
                return;
            }
        }

        if (SignatureHelpPopup.IsOpen && e.Key == Key.Escape)
        {
            CloseSignatureHelpPopup();
            e.Handled = true;
            return;
        }

        if (TryHandleSmartEnter(e))
        {
            e.Handled = true;
            return;
        }

        if (TryHandleMacEditingShortcut(e))
        {
            e.Handled = true;
            return;
        }

        if (MatchesEditorCommand(CommandIds.EditorDuplicateLine, gesture))
        {
            if (TryDuplicateSelectionOrLine())
                e.Handled = true;
            return;
        }

        var caret     = Editor.TextArea.Caret;
        var line      = caret.Line - 1;
        var character = caret.Column - 1;

        if (MatchesEditorCommand(CommandIds.EditorTriggerCompletion, gesture))
        {
            _ = TriggerCompletionAsync(immediate: true);
            e.Handled = true;
        }
        else if (MatchesEditorCommand(CommandIds.EditorGoToDefinition, gesture))
        {
            _viewModel.PublishGoToDefinition(line, character);
            e.Handled = true;
        }
        else if (MatchesEditorCommand(CommandIds.EditorGoToImplementation, gesture))
        {
            _viewModel.PublishGoToImplementation(line, character);
            e.Handled = true;
        }
        else if (MatchesEditorCommand(CommandIds.EditorGoToTypeDefinition, gesture))
        {
            _viewModel.PublishGoToTypeDefinition(line, character);
            e.Handled = true;
        }
    }

    private void OnEditorPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || !IsMacCommandPeriodRelease(e))
            return;

        var gesture = EditorKeyGestureFormatter.FromEvent(e);
        if (!MatchesEditorCommand(CommandIds.EditorQuickFix, gesture))
            return;

        TriggerQuickFix();
        e.Handled = true;
    }

    private bool MatchesEditorCommand(string commandId, string gesture) =>
        _viewModel?.Keybindings.GetKeybindings().Any(binding =>
            string.Equals(binding.Command, commandId, StringComparison.Ordinal)
            && string.Equals(binding.Scope, KeybindingScope.Editor, StringComparison.Ordinal)
            && string.Equals(
                EditorKeyGestureFormatter.Normalize(binding.Key),
                EditorKeyGestureFormatter.Normalize(gesture),
                StringComparison.Ordinal)) == true;

    private static bool IsMacCommandPeriodRelease(KeyEventArgs e) =>
        OperatingSystem.IsMacOS() &&
        e.KeyModifiers.HasFlag(KeyModifiers.Meta) &&
        EditorKeyGestureFormatter.Normalize(EditorKeyGestureFormatter.FromEvent(e)) == "META+.";

    private bool TryHandleAcceleratedUndo(KeyEventArgs e)
    {
        if (e.Key != Key.Z || !IsUndoModifier(e.KeyModifiers))
        {
            ResetUndoAcceleration();
            return false;
        }

        var undoStack = Editor.Document?.UndoStack;
        if (undoStack is null || !undoStack.CanUndo)
            return true;

        var undoCount = GetAcceleratedUndoCount();
        for (var i = 0; i < undoCount && undoStack.CanUndo; i++)
            undoStack.Undo();

        return true;
    }

    private int GetAcceleratedUndoCount()
    {
        var now = DateTime.UtcNow;
        if (now - _lastUndoShortcutAt > TimeSpan.FromMilliseconds(220))
            _undoShortcutRepeatCount = 0;

        _lastUndoShortcutAt = now;
        _undoShortcutRepeatCount++;

        return _undoShortcutRepeatCount switch
        {
            <= 4  => 1,
            <= 8  => 2,
            <= 14 => 4,
            _     => 8,
        };
    }

    private void ResetUndoAcceleration()
    {
        _undoShortcutRepeatCount = 0;
        _lastUndoShortcutAt = DateTime.MinValue;
    }

    private static bool IsUndoModifier(KeyModifiers modifiers)
    {
        var primaryModifier = OperatingSystem.IsMacOS()
            ? KeyModifiers.Meta
            : KeyModifiers.Control;

        return modifiers == primaryModifier;
    }

    private bool TryHandleMacEditingShortcut(KeyEventArgs e)
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        var modifiers = e.KeyModifiers;
        var isCommand = modifiers == KeyModifiers.Meta;
        var isOption  = modifiers == KeyModifiers.Alt;

        if (!isCommand && !isOption)
            return false;

        if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            if (DeleteSelectionIfPresent())
                return true;

            if (isCommand)
            {
                DeleteCurrentLine();
                return true;
            }

            if (isOption || modifiers == KeyModifiers.Control)
            {
                DeleteWordBackward();
                return true;
            }
        }

        if (isCommand && (e.Key == Key.Left || e.Key == Key.Right))
        {
            MoveCaretToLineBoundary(e.Key == Key.Left);
            return true;
        }

        if (isOption && (e.Key == Key.Left || e.Key == Key.Right))
        {
            MoveCaretByWord(e.Key == Key.Left);
            return true;
        }

        return false;
    }

    private bool DeleteSelectionIfPresent()
    {
        var document = Editor.Document;
        if (document is null) return false;

        var selection = Editor.TextArea.Selection;
        if (selection.IsEmpty) return false;

        document.Remove(selection.SurroundingSegment.Offset, selection.SurroundingSegment.Length);
        SetCaretOffset(selection.SurroundingSegment.Offset);
        return true;
    }

    private void DeleteCurrentLine()
    {
        var document = Editor.Document;
        if (document is null) return;

        var caretLine = Editor.TextArea.Caret.Line;
        if (caretLine <= 0 || caretLine > document.LineCount)
            return;

        var line        = document.GetLineByNumber(caretLine);
        var deleteStart = line.Offset;
        var deleteEnd   = Editor.TextArea.Caret.Offset;

        if (deleteEnd <= deleteStart)
        {
            if (caretLine <= 1) return;
            var previousLine = document.GetLineByNumber(caretLine - 1);
            Editor.TextArea.Caret.Offset = previousLine.EndOffset;
            return;
        }

        document.Remove(deleteStart, deleteEnd - deleteStart);
        SetCaretOffset(deleteStart);
    }

    private void DeleteWordBackward()
    {
        var document = Editor.Document;
        if (document is null) return;

        var caretOffset = Editor.TextArea.Caret.Offset;
        var deleteStart = TextUtilities.GetNextCaretPosition(
            document, caretOffset, LogicalDirection.Backward, CaretPositioningMode.WordStartOrSymbol);

        if (deleteStart < 0 || deleteStart >= caretOffset) return;

        document.Remove(deleteStart, caretOffset - deleteStart);
        SetCaretOffset(deleteStart);
    }

    private bool TryDuplicateSelectionOrLine()
    {
        var document = Editor.Document;
        if (document is null)
            return false;

        var textArea = Editor.TextArea;
        var selection = textArea.Selection;
        var caretOffset = textArea.Caret.Offset;
        var handled = false;
        var newCaretOffset = caretOffset;

        document.BeginUpdate();
        try
        {
            if (!selection.IsEmpty)
            {
                var segment = selection.SurroundingSegment;
                var selectedText = document.GetText(segment.Offset, segment.Length);
                document.Insert(segment.Offset + segment.Length, selectedText);
                newCaretOffset = segment.Offset + segment.Length * 2;
                handled = true;
            }
            else
            {
                var caretLine = textArea.Caret.Line;
                if (caretLine <= 0 || caretLine > document.LineCount)
                    return false;

                var line = document.GetLineByNumber(caretLine);
                var lineText = document.GetText(line.Offset, line.Length);
                var lineDelimiter = GetLineDelimiter(document, line.EndOffset);
                var columnOffset = Math.Clamp(caretOffset - line.Offset, 0, line.Length);

                document.Insert(line.EndOffset, lineDelimiter + lineText);
                newCaretOffset = line.EndOffset + lineDelimiter.Length + columnOffset;
                handled = true;
            }
        }
        finally
        {
            document.EndUpdate();
        }

        if (handled)
            SetCaretOffset(newCaretOffset);

        return handled;
    }

    private void MoveCaretToLineBoundary(bool toStart)
    {
        var document = Editor.Document;
        if (document is null) return;

        var caretOffset = Editor.TextArea.Caret.Offset;
        var line        = document.GetLineByOffset(caretOffset);
        SetCaretOffset(toStart ? line.Offset : line.EndOffset);
    }

    private void MoveCaretByWord(bool toStart)
    {
        var document = Editor.Document;
        if (document is null) return;

        var caretOffset = Editor.TextArea.Caret.Offset;
        var direction   = toStart ? LogicalDirection.Backward : LogicalDirection.Forward;
        var newOffset   = TextUtilities.GetNextCaretPosition(
            document, caretOffset, direction, CaretPositioningMode.WordStartOrSymbol);

        if (newOffset >= 0)
            SetCaretOffset(newOffset);
    }

    private static string GetLineDelimiter(TextDocument document, int lineEndOffset)
    {
        if (lineEndOffset >= document.TextLength)
            return Environment.NewLine;

        var current = document.GetCharAt(lineEndOffset);
        if (current == '\r')
        {
            if (lineEndOffset + 1 < document.TextLength && document.GetCharAt(lineEndOffset + 1) == '\n')
                return "\r\n";

            return "\r";
        }

        if (current == '\n')
            return "\n";

        return Environment.NewLine;
    }

    private void SetCaretOffset(int offset)
    {
        Editor.TextArea.Caret.Offset = offset;
        Editor.TextArea.Caret.BringCaretToView();
    }



    private bool TryHandleSmartEnter(KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return false;

        var document   = Editor.Document;
        var activePath = _viewModel?.ActiveDocumentPath;
        if (document is null || string.IsNullOrWhiteSpace(activePath))
            return false;

        if (!string.Equals(Path.GetExtension(activePath), ".cs", StringComparison.OrdinalIgnoreCase))
            return false;

        var textArea = Editor.TextArea;
        if (!textArea.Selection.IsEmpty)
            return false;

        if (CompletionPopup.IsOpen)
            return false;

        var caretOffset = textArea.Caret.Offset;
        if (IsInsideComment(document, caretOffset))
            return false;

        var line       = document.GetLineByOffset(caretOffset);
        var lineText   = document.GetText(line.Offset, line.Length);
        var linePrefix = document.GetText(line.Offset, caretOffset - line.Offset);
        var lineSuffix = document.GetText(caretOffset, line.EndOffset - caretOffset);

        var indent     = GetLineIndent(lineText);
        var baseIndent = indent;
        var indentUnit = GetIndentUnit(indent);

        var trimmedPrefix = linePrefix.TrimEnd();
        var trimmedSuffix = lineSuffix.TrimStart();

        if (trimmedPrefix.EndsWith("{", StringComparison.Ordinal) &&
            trimmedSuffix.StartsWith("}", StringComparison.Ordinal))
        {
            var openBraceRelativeOffset = linePrefix.LastIndexOf('{');
            if (openBraceRelativeOffset < 0)
                return false;

            var openBraceOffset = line.Offset + openBraceRelativeOffset;
            var replacement = string.Concat(
                Environment.NewLine,
                indent,
                "{",
                Environment.NewLine,
                indent,
                indentUnit,
                Environment.NewLine,
                indent,
                "}");

            document.Replace(openBraceOffset, caretOffset + 1 - openBraceOffset, replacement);
            SetCaretOffset(openBraceOffset + Environment.NewLine.Length + indent.Length + 1 + Environment.NewLine.Length + indent.Length + indentUnit.Length);
            return true;
        }

        if (trimmedPrefix.EndsWith("{", StringComparison.Ordinal))
        {
            var text = $"{Environment.NewLine}{indent}{indentUnit}";
            document.Insert(caretOffset, text);
            SetCaretOffset(caretOffset + text.Length);
            return true;
        }

        if (lineText.TrimStart().StartsWith("}", StringComparison.Ordinal) &&
            caretOffset <= line.Offset + (lineText.Length - lineText.TrimStart().Length))
        {
            var dedented = DedentIndent(baseIndent, indentUnit);
            var text     = $"{Environment.NewLine}{dedented}";
            document.Insert(caretOffset, text);
            SetCaretOffset(caretOffset + text.Length);
            return true;
        }

        return false;
    }

    private static string GetLineIndent(string lineText)
    {
        var index = 0;
        while (index < lineText.Length && char.IsWhiteSpace(lineText[index]))
            index++;

        return index > 0 ? lineText[..index] : string.Empty;
    }

    private static string GetIndentUnit(string currentIndent) =>
        currentIndent.Contains('\t') ? "\t" : "    ";

    private static string DedentIndent(string currentIndent, string indentUnit)
    {
        if (string.IsNullOrEmpty(currentIndent))
            return string.Empty;

        if (currentIndent.EndsWith(indentUnit, StringComparison.Ordinal))
            return currentIndent[..^indentUnit.Length];

        if (currentIndent.EndsWith("\t", StringComparison.Ordinal))
            return currentIndent[..^1];

        var spacesToRemove = Math.Min(4, currentIndent.Length);
        return spacesToRemove > 0 ? currentIndent[..^spacesToRemove] : string.Empty;
    }

    private bool IsInsideComment(TextDocument document, int caretOffset)
    {
        if (caretOffset <= 0)
            return false;

        var currentLine = document.GetLineByOffset(caretOffset);

        // Phase A: rebuild pre-line cache only when caret moved to a different line.
        // For forward movement (common: Enter key), scan only the delta from the old cached line.
        // For backward movement or cache miss, resume from the nearest checkpoint.
        if (currentLine.LineNumber != _blockCommentCacheLineNumber)
        {
            bool state;
            var startLine = 1;
            if (_blockCommentCacheLineNumber > 0 && currentLine.LineNumber > _blockCommentCacheLineNumber)
            {
                state = _cachedBlockCommentState;
                startLine = _blockCommentCacheLineNumber;
            }
            else
            {
                var checkpointLine = FindBlockCommentCheckpointLine(currentLine.LineNumber);
                state = checkpointLine > 0 && _blockCommentCheckpoints.TryGetValue(checkpointLine, out var checkpointState)
                    ? checkpointState
                    : false;
                startLine = checkpointLine > 0 ? checkpointLine : 1;
            }

            for (var ln = startLine; ln < currentLine.LineNumber; ln++)
            {
                if ((ln - 1) % BlockCommentCheckpointInterval == 0)
                    _blockCommentCheckpoints[ln] = state;

                var l = document.GetLineByNumber(ln);
                state = ScanLineForBlockCommentState(document.GetText(l.Offset, l.Length), l.Length, state);

                var nextLine = ln + 1;
                if ((nextLine - 1) % BlockCommentCheckpointInterval == 0)
                {
                    _blockCommentCheckpoints[nextLine] = state;
                }
            }
            _cachedBlockCommentState = state;
            _blockCommentCacheLineNumber = currentLine.LineNumber;
        }

        // Phase B: scan only the current line up to the caret — O(line_length)
        var lineText = document.GetText(currentLine.Offset, currentLine.Length);
        var limit    = Math.Min(caretOffset - currentLine.Offset, lineText.Length);
        return ScanLineForBlockCommentState(lineText, limit, _cachedBlockCommentState);
    }

    private int FindBlockCommentCheckpointLine(int lineNumber)
    {
        var checkpointLine = ((lineNumber - 1) / BlockCommentCheckpointInterval) * BlockCommentCheckpointInterval + 1;
        while (checkpointLine > 1)
        {
            if (_blockCommentCheckpoints.ContainsKey(checkpointLine))
                return checkpointLine;

            checkpointLine -= BlockCommentCheckpointInterval;
        }

        return 1;
    }

    private void ResetBlockCommentCache()
    {
        _blockCommentCheckpoints.Clear();
        _cachedBlockCommentState = false;
        _blockCommentCacheLineNumber = -1;
    }

    private static bool ScanLineForBlockCommentState(string lineText, int limit, bool inBlockComment)
    {
        for (var i = 0; i < limit; i++)
        {
            var ch   = lineText[i];
            var next = i + 1 < limit ? lineText[i + 1] : '\0';

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (ch == '/' && next == '/')
                break;

            if (ch == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
            }
        }
        return inBlockComment;
    }
}
