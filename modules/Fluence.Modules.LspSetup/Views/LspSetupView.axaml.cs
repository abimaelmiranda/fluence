using Avalonia.Controls;

namespace Fluence.Modules.LspSetup.Views;

public partial class LspSetupView : UserControl
{
    public LspSetupView()
    {
        InitializeComponent();
        var scroller = this.FindControl<ScrollViewer>("LogScroller");
        if (scroller is not null)
            scroller.PropertyChanged += (_, e) =>
            {
                if (e.Property == ScrollViewer.ExtentProperty)
                    scroller.ScrollToEnd();
            };
    }
}
