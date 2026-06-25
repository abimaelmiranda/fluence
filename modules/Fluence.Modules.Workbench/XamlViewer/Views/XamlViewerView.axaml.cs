using System.ComponentModel;
using Avalonia.Controls;
using Fluence.Modules.Workbench.XamlViewer.ViewModels;

namespace Fluence.Modules.Workbench.XamlViewer.Views;

public partial class XamlViewerView : UserControl
{
    private XamlViewerViewModel? _subscribedVm;

    public XamlViewerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;

        _subscribedVm = DataContext as XamlViewerViewModel;

        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(XamlViewerViewModel.PreviewContent)) return;

        var content = (DataContext as XamlViewerViewModel)?.PreviewContent;
        if (content != null)
            content.DataContext = null;
        PreviewHost.Content = content;
    }
}
