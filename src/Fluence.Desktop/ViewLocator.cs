using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Fluence.Core.Modules;
using Fluence.Core.ViewModels;

namespace Fluence.Desktop;

public class ViewLocator(IViewRegistry registry) : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var viewType = registry.Resolve(param.GetType());
        if (viewType is null)
            return new TextBlock { Text = $"No view registered for {param.GetType().Name}" };

        return (Control)System.Activator.CreateInstance(viewType)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
