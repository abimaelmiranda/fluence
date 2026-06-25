using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.LanguageServer;
using Fluence.Core.Events.Lsp;

namespace Fluence.Modules.Workbench.Editor.Views;

public partial class EditorView
{
    private async Task TriggerSignatureHelpAsync(char triggerChar)
    {
        if (_signatureHelpService is null || _viewModel?.ActiveDocumentPath is null)
            return;

        var caret       = Editor.TextArea.Caret;
        var filePath    = _viewModel.ActiveDocumentPath;
        var position    = caret.Position;
        var line        = position.Line - 1;
        var character   = position.Column - 1;
        var caretOffset = caret.Offset;
        var isRetrigger = SignatureHelpPopup.IsOpen;
        var version     = Interlocked.Increment(ref _signatureHelpVersion);

        _viewModel.EventBus.Publish(new LspInteractiveRequestStartedEvent(filePath));
        _viewModel.PublishLiveDocumentChanged(Editor.Text, flushImmediately: true);

        if (!isRetrigger)
        {
            _signatureTriggerOffset = caretOffset;
            Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChangedForSignatureHelp;
            Editor.TextArea.Caret.PositionChanged += OnCaretPositionChangedForSignatureHelp;
        }

        _viewModel.TaskScheduler.Schedule(
            $"editor.signature.{filePath}",
            TaskPriority.Input,
            ct => ProcessSignatureHelpAsync(filePath, line, character, triggerChar, isRetrigger, version, ct),
            correlationId: version);
        await Task.CompletedTask;
    }

    private async Task ProcessSignatureHelpAsync(
        string filePath,
        int line,
        int character,
        char triggerChar,
        bool isRetrigger,
        int version,
        CancellationToken ct)
    {
        try
        {
            if (_signatureHelpService is null || ct.IsCancellationRequested)
                return;

            var sigHelp = await _signatureHelpService.GetSignatureHelpAsync(
                filePath, line, character, isRetrigger, triggerChar, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested || version != Volatile.Read(ref _signatureHelpVersion))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested || version != Volatile.Read(ref _signatureHelpVersion))
                    return;

                if (sigHelp is null || sigHelp.Signatures.Count == 0)
                {
                    CloseSignatureHelpPopup();
                    return;
                }

                RenderSignatureHelp(sigHelp);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EditorView/SignatureHelp] {ex.Message}"); }
    }

    private void OnCaretPositionChangedForSignatureHelp(object? sender, EventArgs e)
    {
        if (!SignatureHelpPopup.IsOpen || _signatureTriggerOffset < 0)
            return;
        if (Editor.TextArea.Caret.Offset < _signatureTriggerOffset ||
            !IsCaretInsideSignatureInvocation())
        {
            Dispatcher.UIThread.Post(CloseSignatureHelpPopup, DispatcherPriority.Background);
        }
    }

    private void CloseSignatureHelpPopup()
    {
        if (!SignatureHelpPopup.IsOpen) return;
        Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChangedForSignatureHelp;
        SignatureHelpPopup.IsOpen = false;
        _signatureTriggerOffset = -1;
        SignatureHelpDocumentationText.Text = string.Empty;
        SignatureHelpDocumentationScroll.IsVisible = false;
        Interlocked.Increment(ref _signatureHelpVersion);
    }

    private void RenderSignatureHelp(LspSignatureHelp sigHelp)
    {
        var sigIndex   = Math.Clamp(sigHelp.ActiveSignature, 0, sigHelp.Signatures.Count - 1);
        var sig        = sigHelp.Signatures[sigIndex];
        var activeParam = sigHelp.ActiveParameter;
        var parameters = sig.Parameters;

        SignatureHelpText.Inlines?.Clear();

        if (parameters is null || parameters.Count == 0 || activeParam < 0 || activeParam >= parameters.Count)
            SignatureHelpText.Inlines?.Add(new Run(sig.Label));
        else
            BuildSignatureInlines(sig, activeParam);

        RenderSignatureDocumentation(sig, activeParam);
        PositionSignatureHelpPopup();
        SignatureHelpPopup.IsOpen = true;
    }

    private void BuildSignatureInlines(LspSignatureInformation sig, int activeParamIndex)
    {
        var inlines = SignatureHelpText.Inlines;
        if (inlines is null) return;
        inlines.Clear();

        var label       = sig.Label;
        var activeParam = sig.Parameters![activeParamIndex];

        int paramStart, paramEnd;
        if (activeParam.LabelStart.HasValue && activeParam.LabelEnd.HasValue)
        {
            paramStart = activeParam.LabelStart.Value;
            paramEnd   = activeParam.LabelEnd.Value;
        }
        else if (!string.IsNullOrEmpty(activeParam.Label))
        {
            paramStart = label.IndexOf(activeParam.Label, StringComparison.Ordinal);
            paramEnd   = paramStart >= 0 ? paramStart + activeParam.Label.Length : -1;
        }
        else
        {
            paramStart = paramEnd = -1;
        }

        if (paramStart < 0 || paramEnd <= paramStart || paramStart >= label.Length)
        {
            inlines.Add(new Run(label) { Foreground = SignatureGrayBrush });
            return;
        }

        paramEnd = Math.Min(paramEnd, label.Length);

        if (paramStart > 0)
            inlines.Add(new Run(label[..paramStart]) { Foreground = SignatureGrayBrush });

        inlines.Add(new Run(label[paramStart..paramEnd]) { Foreground = SignatureWhiteBrush, FontWeight = FontWeight.Bold });

        if (paramEnd < label.Length)
            inlines.Add(new Run(label[paramEnd..]) { Foreground = SignatureGrayBrush });
    }

    private void RenderSignatureDocumentation(LspSignatureInformation sig, int activeParamIndex)
    {
        var documentation = sig.Documentation;
        if (sig.Parameters is not null &&
            activeParamIndex >= 0 &&
            activeParamIndex < sig.Parameters.Count &&
            !string.IsNullOrWhiteSpace(sig.Parameters[activeParamIndex].Documentation))
        {
            documentation = string.IsNullOrWhiteSpace(documentation)
                ? sig.Parameters[activeParamIndex].Documentation
                : $"{documentation}\n\n{sig.Parameters[activeParamIndex].Documentation}";
        }

        documentation = NormalizeSignatureDocumentation(documentation);
        SignatureHelpDocumentationText.Text = documentation;
        SignatureHelpDocumentationScroll.IsVisible = !string.IsNullOrWhiteSpace(documentation);
    }

    private static string NormalizeSignatureDocumentation(string? documentation)
    {
        if (string.IsNullOrWhiteSpace(documentation))
            return string.Empty;

        return documentation
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private bool IsCaretInsideSignatureInvocation()
    {
        var document = Editor.Document;
        if (document is null)
            return false;

        var caretOffset = Math.Clamp(Editor.TextArea.Caret.Offset, 0, document.TextLength);
        var text = document.Text;
        var depth = 0;

        for (var i = caretOffset - 1; i >= 0; i--)
        {
            var ch = text[i];
            if (ch == ')')
            {
                depth++;
                continue;
            }

            if (ch == '(')
            {
                if (depth == 0)
                    return true;
                depth--;
                continue;
            }

            if (ch is ';' or '{' or '}')
                return false;
        }

        return false;
    }

    private void PositionSignatureHelpPopup()
    {
        var textView = Editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var caretPos = Editor.TextArea.Caret.Position;
        if (textView.GetVisualLine(caretPos.Line) is null)
            return;

        var visualPos    = textView.GetVisualPosition(caretPos, VisualYPosition.LineTop);
        var scrollOffset = textView.ScrollOffset;
        SignatureHelpPopup.PlacementTarget = textView;
        SignatureHelpPopup.PlacementRect = new Rect(
            visualPos.X - scrollOffset.X,
            visualPos.Y - scrollOffset.Y,
            1, 1);
    }
}
