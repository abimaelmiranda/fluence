using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Dotnet;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.DotnetCli.ViewModels;

public sealed partial class DotnetSdkSetupViewModel : ViewModelBase
{
    private readonly IDotnetSdkProvisioningService _provisioning;
    private readonly IShellEventBus _events;

    public DotnetSdkSetupViewModel(IDotnetSdkProvisioningService provisioning, IShellEventBus events)
    {
        _provisioning = provisioning;
        _events = events;
    }

    public ObservableCollection<DotnetSdkOptionViewModel> SdkOptions { get; } =
    [
        new(".NET 10 SDK", "10.0", "Recommended for Fluence projects targeting net10.0.", true),
        new(".NET 9 SDK", "9.0", "Install when working with net9.0 projects.", false),
        new(".NET 8 SDK", "8.0", "Install for LTS projects targeting net8.0.", false),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallOfficialCommand))]
    private DotnetSdkOptionViewModel? _selectedSdkOption;

    [ObservableProperty]
    private string _recommendedVersion = ".NET SDK";

    [ObservableProperty]
    private string _dotnetPath = "Not detected";

    [ObservableProperty]
    private string _ideManagedInstallPath = string.Empty;

    [ObservableProperty]
    private string _status = "Checking .NET SDK...";

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallOfficialCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasSdk;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        SelectedSdkOption ??= SdkOptions.FirstOrDefault(option => option.IsRecommended)
                              ?? SdkOptions.FirstOrDefault();
        IsRunning = true;
        Status = "Checking .NET SDK...";

        try
        {
            var status = await _provisioning.GetStatusAsync(cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => ApplyStatus(status));
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task RefreshSdkAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanInstallSelectedSdk))]
    private async Task InstallOfficialAsync(CancellationToken cancellationToken)
    {
        SelectedSdkOption ??= SdkOptions.FirstOrDefault(option => option.IsRecommended)
                              ?? SdkOptions.FirstOrDefault();
        if (SelectedSdkOption is null || SelectedSdkOption.IsInstalled)
            return;

        IsRunning = true;
        try
        {
            await _provisioning.InstallIdeManagedSdkAsync(
                SelectedSdkOption.Channel,
                SelectedSdkOption.DisplayName,
                AppendOutput,
                cancellationToken);
            await RefreshAsync(cancellationToken);
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task RetrySystemWideAsync(CancellationToken cancellationToken)
    {
        IsRunning = true;
        try
        {
            await _provisioning.OpenSystemWideInstallerAsync(AppendOutput, cancellationToken);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void ApplyStatus(DotnetSdkStatus sdkStatus)
    {
        foreach (var option in SdkOptions)
        {
            var installed = sdkStatus.InstalledSdks
                .FirstOrDefault(sdk => sdk.Version.StartsWith(option.Channel, StringComparison.OrdinalIgnoreCase));
            option.IsInstalled = installed is not null;
            option.InstalledVersion = installed?.Version ?? string.Empty;
            option.InstalledPath = installed?.Path ?? string.Empty;
        }

        if (SelectedSdkOption is null || SelectedSdkOption.IsInstalled)
        {
            SelectedSdkOption = SdkOptions.FirstOrDefault(option => !option.IsInstalled && option.IsRecommended)
                                ?? SdkOptions.FirstOrDefault(option => !option.IsInstalled)
                                ?? SdkOptions.FirstOrDefault();
        }

        RecommendedVersion = sdkStatus.RecommendedVersion;
        DotnetPath = sdkStatus.DotnetPath ?? "Not detected";
        IdeManagedInstallPath = sdkStatus.IdeManagedInstallPath;
        HasSdk = sdkStatus.InstalledSdks.Count > 0;
        Status = HasSdk
            ? $"Using {Path.GetFileName(DotnetPath)} from {Path.GetDirectoryName(DotnetPath)}"
            : "No .NET SDK detected.";

        if (!string.IsNullOrWhiteSpace(sdkStatus.ErrorMessage))
            AppendOutput($"[dotnet] {sdkStatus.ErrorMessage}");

        _events.Publish(new DotnetSdkChangedEvent());
        InstallOfficialCommand.NotifyCanExecuteChanged();
    }

    private bool CanInstallSelectedSdk() => !IsRunning && SelectedSdkOption is { IsInstalled: false };

    private void AppendOutput(string line)
    {
        Dispatcher.UIThread.Post(() => Output += line + Environment.NewLine);
    }
}
