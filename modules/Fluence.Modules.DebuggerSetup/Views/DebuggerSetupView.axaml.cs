using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Fluence.Modules.DebuggerSetup.Views;

public partial class DebuggerSetupView : UserControl
{
    public DebuggerSetupView()
    {
        InitializeComponent();
        // Auto-scroll the log to the bottom when new content is appended
        var scroller = this.FindControl<ScrollViewer>("LogScroller");
        if (scroller is not null)
            scroller.PropertyChanged += (_, e) =>
            {
                if (e.Property == ScrollViewer.ExtentProperty)
                    scroller.ScrollToEnd();
            };
    }
}
