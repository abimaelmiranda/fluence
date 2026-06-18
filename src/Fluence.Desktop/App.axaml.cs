using System;
using System.Diagnostics;
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
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Services.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Desktop;

public partial class App : Avalonia.Application
{
    private static readonly TimeSpan ServiceProviderDisposeTimeout = TimeSpan.FromSeconds(2);

    private ServiceProvider? _serviceProvider;
    private int _isShowingFatalException;
    private bool _isShuttingDown;
    private bool _shutdownCompleted;

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
                if (_shutdownCompleted)
                    return;

                e.Cancel = true;
                if (_isShuttingDown)
                    return;

                _isShuttingDown = true;
                mainWindowViewModel.IsShutdownOverlayVisible = true;
                mainWindowViewModel.ShutdownStatusText = "Saving workspace...";

                try
                {
                    var serviceProvider = _serviceProvider;
                    if (serviceProvider is null)
                    {
                        _shutdownCompleted = true;
                        mainWindow.Close();
                        return;
                    }

                    if (snapshotCoordinator.HasWorkspaceToSave)
                    {
                        await Task.WhenAll(
                            snapshotCoordinator.SaveAsync(),
                            Task.Delay(500));
                    }

                    mainWindowViewModel.ShutdownStatusText = "Stopping modules...";
                    using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var progress = new Progress<ModuleShutdownProgress>(p =>
                    {
                        mainWindowViewModel.ShutdownStatusText = p.Message;
                    });

                    await serviceProvider.GetRequiredService<IShutdownCoordinator>()
                        .ShutdownAsync(progress, shutdownCts.Token);

                    mainWindowViewModel.ShutdownStatusText = "Finalizing shutdown...";
                }
                finally
                {
                    _shutdownCompleted = true;
                    mainWindow.Close();
                }
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

        var shutdown = _serviceProvider.GetRequiredService<IShutdownCoordinator>();
        if (!shutdown.IsShutdownComplete)
        {
            shutdown.ShutdownAsync()
                .GetAwaiter()
                .GetResult();
            _shutdownCompleted = true;
        }

        DisposeServiceProviderBestEffort(_serviceProvider);
        _serviceProvider = null;
    }

    private static void DisposeServiceProviderBestEffort(ServiceProvider serviceProvider)
    {
        try
        {
            Task.Run(() =>
                {
                    serviceProvider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                })
                .WaitAsync(ServiceProviderDisposeTimeout)
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            Debug.WriteLine($"Service provider dispose timed out after {ServiceProviderDisposeTimeout.TotalMilliseconds}ms.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Service provider dispose failed: {ex}");
        }
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
