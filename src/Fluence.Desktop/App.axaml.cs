using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Fluence.Desktop.Composition;
using Fluence.Desktop.Services;
using Fluence.Desktop.ViewModels;
using Fluence.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Desktop;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;
    private int _isShowingFatalException;

    public override void Initialize()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _serviceProvider = Bootstrapper.BuildServices();
            Bootstrapper.InitializeModules(_serviceProvider);

            var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
            };
            _serviceProvider.GetRequiredService<AvaloniaUserNotificationService>().Attach(mainWindow);

            NativeMenu.SetMenu(mainWindow, NativeMenus.CreateMainMenu(mainWindowViewModel));

            desktop.MainWindow = mainWindow;
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        // TerminalService implements only IAsyncDisposable; call DisposeAsync to avoid InvalidOperationException.
        (_serviceProvider as System.IAsyncDisposable)?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _serviceProvider = null;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFatalException(e.Exception);
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString());
        Dispatcher.UIThread.Post(() => ShowFatalException(exception));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Dispatcher.UIThread.Post(() => ShowFatalException(e.Exception));
    }

    private void ShowFatalException(Exception exception)
    {
        if (Interlocked.Exchange(ref _isShowingFatalException, 1) == 1)
        {
            return;
        }

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            Environment.Exit(1);
            return;
        }

        var dialog = new CrashDialog(exception.ToString());
        if (desktop.MainWindow is null)
        {
            dialog.Show();
            return;
        }

        _ = dialog.ShowDialog(desktop.MainWindow);
    }
}
