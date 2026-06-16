using Avalonia;
using System;

namespace Fluence.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveModuleAssembly;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static System.Reflection.Assembly? ResolveModuleAssembly(object? sender, ResolveEventArgs e)
    {
        var name = new System.Reflection.AssemblyName(e.Name).Name + ".dll";
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "modules", name);
        return System.IO.File.Exists(path) ? System.Reflection.Assembly.LoadFrom(path) : null;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
