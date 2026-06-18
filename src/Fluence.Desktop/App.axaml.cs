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
using Fluence.Infrastructure;
using Fluence.Core.Abstractions.Keybindings;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Desktop;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;
    private int _isShowingFatalException;
    private bool _isSavingWorkspace;

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
            DataTemplates.Add(new ViewLocator());
            Bootstrapper.InitializeModules(_serviceProvider);

            // Resolve eagerly to subscribe to workspace.Changed for auto-save
            var snapshotCoordinator = _serviceProvider.GetRequiredService<WorkspaceSnapshotCoordinator>();

            var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
            };
            _serviceProvider.GetRequiredService<AvaloniaUserNotificationService>().Attach(mainWindow);

            NativeMenu.SetMenu(
                mainWindow,
                NativeMenus.CreateMainMenu(
                    mainWindowViewModel,
                    _serviceProvider.GetRequiredService<IKeybindingService>()));


            mainWindow.Closing += async (_, e) =>
            {
                if (_isSavingWorkspace)
                    return; // save finished, allow close

                if (!snapshotCoordinator.HasWorkspaceToSave)
                    return; // nothing to save, allow close immediately

                e.Cancel = true;
                _isSavingWorkspace = true;
                mainWindowViewModel.IsSavingWorkspace = true;

                await Task.WhenAll(
                    snapshotCoordinator.SaveAsync(),
                    Task.Delay(500));

                mainWindow.Close();
            };

            desktop.MainWindow = mainWindow;
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnAboutClicked(object? sender, EventArgs e)
    {
        var mainWindow = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var dialog = new Views.AboutDialog();
        if (mainWindow is not null)
            _ = dialog.ShowDialog(mainWindow);
        else
            dialog.Show();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_serviceProvider is null)
            return;

        // TODO: Investigate macOS Cmd+Q shutdown path. Closing with the traffic-light button exits cleanly,
        // but Cmd+Q currently reports a managed unhandled exception as SIGABRT in macOS crash reporter.
        // Synchronous block required: async void does not hold the process alive long enough
        // for the service provider to finish disposing (OmniSharp would be left as an orphan process).
        _serviceProvider.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
