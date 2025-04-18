using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepersCompound.LGS;
using KeepersCompound.Lighting;
using Serilog;

namespace KCLight.Gui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _installPath = "";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _campaignName = "";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _missionName = "";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _outputName = "kc_lit";

    [ObservableProperty] private bool _fastPvs;
    [ObservableProperty] private bool _validInstallPath;
    [ObservableProperty] private bool _validCampaignName;
    [ObservableProperty] private bool _validMissionName;
    [ObservableProperty] private List<string> _campaignNames = [];
    [ObservableProperty] private List<string> _missionNames = [];

    private ResourcePathManager? _pathManager;

    partial void OnInstallPathChanged(string value)
    {
        var tmpDir = Directory.CreateTempSubdirectory("KCLightmapper");
        var pathManager = new ResourcePathManager(tmpDir.FullName);

        ValidInstallPath = pathManager.TryInit(InstallPath);
        if (ValidInstallPath)
        {
            Log.Information("Path manager initialised successfully");
            _pathManager = pathManager;
            CampaignNames = _pathManager.GetCampaignNames();
        }

        ValidateCampaignName();
        ValidateMissionName();
    }

    partial void OnCampaignNameChanged(string value)
    {
        ValidateCampaignName();
        ValidateMissionName();
    }

    partial void OnMissionNameChanged(string value)
    {
        ValidateMissionName();
    }

    private void ValidateCampaignName()
    {
        if (!ValidInstallPath || _pathManager == null)
        {
            ValidCampaignName = false;
            return;
        }

        ValidCampaignName = CampaignName == "" || _pathManager.GetCampaignNames().Contains(CampaignName);
        if (ValidCampaignName)
        {
            MissionNames = _pathManager.GetCampaign(CampaignName).GetResourceNames(ResourceType.Mission);
        }
    }

    private void ValidateMissionName()
    {
        if (!ValidInstallPath || !ValidCampaignName || _pathManager == null)
        {
            ValidMissionName = false;
            return;
        }

        try
        {
            var campaign = _pathManager.GetCampaign(CampaignName);
            var missions = campaign.GetResourceNames(ResourceType.Mission);
            ValidMissionName = missions.Contains(MissionName.ToLower());
        }
        catch
        {
            ValidMissionName = false;
        }
    }

    [RelayCommand(CanExecute = nameof(ValidMissionName))]
    private async Task RunAsync()
    {
        var outputName = OutputName;
        await Task.Run(() =>
        {
            if (_pathManager == null)
            {
                Log.Error("Invalid path manager");
                throw new Exception("Invalid path manager");
            }

            var lightMapper = new LightMapper(_pathManager, CampaignName, MissionName);
            lightMapper.Light(FastPvs);
            lightMapper.Save(outputName);
        });
    }

    [RelayCommand]
    private async Task SelectGameDirectory()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is not { } provider)
        {
            throw new NullReferenceException("Missing StorageProvider instance.");
        }

        var options = new FolderPickerOpenOptions
        {
            Title = "Select Game Directory",
            AllowMultiple = false,
            SuggestedStartLocation = await provider.TryGetFolderFromPathAsync(InstallPath)
        };

        var folders = await provider.OpenFolderPickerAsync(options);
        if (folders.Count > 0)
        {
            InstallPath = folders[0].Path.LocalPath;
        }
    }

    public void Close()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopApp)
        {
            desktopApp.Shutdown();
        }
    }
}