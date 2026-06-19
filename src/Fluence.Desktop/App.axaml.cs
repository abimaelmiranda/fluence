using System;
using System.Diagnostics;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Models.Settings;
using Fluence.Desktop.Composition;
using Fluence.Desktop.Markup;
using Fluence.Desktop.Services;
using Fluence.Desktop.ViewModels;
using Fluence.Desktop.Views;
using Fluence.Infrastructure;
using Fluence.Core.Abstractions.Lifecycle;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Desktop;

public partial class App : Avalonia.Application
{
    private static readonly TimeSpan ServiceProviderDisposeTimeout = TimeSpan.FromSeconds(2);

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
            var localization = _serviceProvider.GetRequiredService<ILocalizationService>();
            localization.Register(new ResourceManager("Fluence.Desktop.Resources.Strings", typeof(App).Assembly));
            Locale.Initialize(localization);

            var savedLanguage = _serviceProvider.GetRequiredService<ISettingsService>().Get<GlobalSettings>().Language;
            localization.SetLanguage(savedLanguage);

            var aboutMenuItem = NativeMenu.GetMenu(this)?.Items.OfType<NativeMenuItem>().FirstOrDefault();
            if (aboutMenuItem is not null)
            {
                aboutMenuItem.Header = localization.Get("Desktop.Menu.About");
                localization.LanguageChanged += () => Dispatcher.UIThread.Post(() =>
                    aboutMenuItem.Header = localization.Get("Desktop.Menu.About"));
            }

            DataTemplates.Add(new ViewLocator());

            // Resolve eagerly to subscribe to workspace.Changed for auto-save
            _serviceProvider.GetRequiredService<WorkspaceSnapshotCoordinator>();
            var lifecycle = _serviceProvider.GetRequiredService<AvaloniaApplicationLifecycleService>();
            lifecycle.Attach(desktop);
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
            };
            _serviceProvider.GetRequiredService<AvaloniaUserNotificationService>().Attach(mainWindow);

            var keybindings = _serviceProvider.GetRequiredService<IKeybindingService>();
            var mainMenu = NativeMenus.CreateMainMenu(mainWindowViewModel, keybindings);
            NativeMenu.SetMenu(mainWindow, mainMenu.Menu);
            localization.LanguageChanged += () => Dispatcher.UIThread.Post(mainMenu.Refresh);


            lifecycle.StatusChanged += (_, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    mainWindowViewModel.ShutdownStatusText = e.Message;
                });
            };

            mainWindow.Closing += async (_, e) =>
            {
                try
                {
                    if (lifecycle.IsShutdownComplete)
                        return;

                    e.Cancel = true;
                    if (lifecycle.IsShutdownInProgress)
                        return;

                    mainWindowViewModel.IsShutdownOverlayVisible = true;
                    await lifecycle.RequestShutdownAsync(
                        new ApplicationShutdownRequest(ApplicationShutdownReason.WindowClose));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[lifecycle] Closing handler failed: {ex}");
                }
            };

            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += OnDesktopShutdownRequested;
            desktop.Exit += OnDesktopExit;

            _ = StartApplicationAsync(_serviceProvider);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartApplicationAsync(IServiceProvider serviceProvider)
    {
        try
        {
            await serviceProvider.GetRequiredService<IStartupCoordinator>()
                .StartAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[startup] Application startup failed: {ex}");
            ShowFatalException(ex);
        }
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

    private void OnDesktopShutdownRequested(
        object? sender,
        Avalonia.Controls.ApplicationLifetimes.ShutdownRequestedEventArgs e)
    {
        if (_serviceProvider is null)
            return;

        var lifecycle = _serviceProvider.GetRequiredService<IApplicationLifecycleService>();
        if (lifecycle.IsShutdownComplete)
            return;

        e.Cancel = true;

        if (lifecycle.IsShutdownInProgress)
            return;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel vm })
            vm.IsShutdownOverlayVisible = true;

        try
        {
            var task = lifecycle.RequestShutdownAsync(new ApplicationShutdownRequest(ApplicationShutdownReason.ApplicationQuit));
            task.ContinueWith(
                t => Debug.WriteLine($"[lifecycle] shutdown request faulted: {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[lifecycle] OnDesktopShutdownRequested failed synchronously: {ex}");
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownRequested -= OnDesktopShutdownRequested;

        Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_serviceProvider is null)
            return;

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
        if (IsExpectedShutdownCancellation(e.Exception))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        ShowFatalException(e.Exception);
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString());
        if (IsExpectedShutdownCancellation(exception))
            return;

        Dispatcher.UIThread.Post(() => ShowFatalException(exception));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        if (IsExpectedShutdownCancellation(e.Exception))
            return;

        Dispatcher.UIThread.Post(() => ShowFatalException(e.Exception));
    }

    private bool IsExpectedShutdownCancellation(Exception exception)
    {
        if (_serviceProvider?.GetService<IApplicationLifecycleService>() is not { } lifecycle)
            return false;

        if (!lifecycle.IsShutdownInProgress && !lifecycle.IsShutdownComplete)
            return false;

        if (exception is OperationCanceledException)
            return true;

        return exception is AggregateException aggregate &&
            aggregate.Flatten().InnerExceptions.All(static ex => ex is OperationCanceledException);
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
