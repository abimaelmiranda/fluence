using System.Diagnostics;
using Avalonia.Controls;
using AvaloniaEdit.TextMate;
using Fluence.Modules.SourceControl.ViewModels;
using TextMateSharp.Grammars;

namespace Fluence.Modules.SourceControl.Views;

public partial class DiffViewerView : UserControl
{
    private TextMate.Installation? _textMateInstallation;

    public DiffViewerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        InitializeTextMate();
    }

    private void InitializeTextMate()
    {
        var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        _textMateInstallation = Editor.InstallTextMate(registryOptions);
        try
        {
            _textMateInstallation.SetGrammar("source.diff");
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"Diff grammar not available; using plain text fallback: {ex}");
        }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is DiffViewerViewModel vm)
            Editor.Text = vm.DiffContent;
    }
}
