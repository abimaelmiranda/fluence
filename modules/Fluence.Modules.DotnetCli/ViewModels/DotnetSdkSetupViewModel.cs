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
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Models.Dotnet;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.DotnetCli.ViewModels;

public sealed partial class DotnetSdkSetupViewModel : ViewModelBase
{
    private readonly IDotnetSdkProvisioningService _provisioning;
    private readonly ILocalizationService _loc;

    public DotnetSdkSetupViewModel(
        IDotnetSdkProvisioningService provisioning,
        ILocalizationService loc)
    {
        _provisioning = provisioning;
        _loc = loc;
        SdkOptions =
        [
            new(".NET 10 SDK", "10.0", _loc.Get("DotnetCli.Sdk.Description.Net10"), true, _loc),
            new(".NET 9 SDK", "9.0", _loc.Get("DotnetCli.Sdk.Description.Net9"), false, _loc),
            new(".NET 8 SDK", "8.0", _loc.Get("DotnetCli.Sdk.Description.Net8"), false, _loc),
        ];
        Status = _loc.Get("DotnetCli.Status.Checking");
    }

    public ObservableCollection<DotnetSdkOptionViewModel> SdkOptions { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallOfficialCommand))]
    private DotnetSdkOptionViewModel? _selectedSdkOption;

    [ObservableProperty]
    private string _recommendedVersion = ".NET SDK";

    [ObservableProperty]
    private string _dotnetPath = string.Empty;

    [ObservableProperty]
    private string _ideManagedInstallPath = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

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
        Status = _loc.Get("DotnetCli.Status.Checking");

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
        DotnetPath = sdkStatus.DotnetPath ?? _loc.Get("DotnetCli.Status.NotDetected");
        IdeManagedInstallPath = sdkStatus.IdeManagedInstallPath;
        HasSdk = sdkStatus.InstalledSdks.Count > 0;
        Status = HasSdk
            ? string.Format(_loc.Get("DotnetCli.Status.UsingDotnetFrom"), Path.GetFileName(DotnetPath), Path.GetDirectoryName(DotnetPath))
            : _loc.Get("DotnetCli.Status.NoSdkDetected");

        if (!string.IsNullOrWhiteSpace(sdkStatus.ErrorMessage))
            AppendOutput($"[dotnet] {sdkStatus.ErrorMessage}");

        InstallOfficialCommand.NotifyCanExecuteChanged();
    }

    private bool CanInstallSelectedSdk() => !IsRunning && SelectedSdkOption is { IsInstalled: false };

    private void AppendOutput(string line)
    {
        Dispatcher.UIThread.Post(() => Output += line + Environment.NewLine);
    }
}
