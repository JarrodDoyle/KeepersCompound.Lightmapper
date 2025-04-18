using System;
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

    [ObservableProperty] private bool _validInstallPath;

    public bool FastPvs { get; set; }

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
        }
    }

    public bool CanRun()
    {
        if (!ValidInstallPath || _pathManager == null)
        {
            return false;
        }

        try
        {
            var campaign = _pathManager.GetCampaign(CampaignName);
            var missions = campaign.GetResourceNames(ResourceType.Mission);
            var validMission = missions.Contains(MissionName.ToLower());
            if (validMission)
            {
                Log.Information("Woo valid mission!");
            }

            return validMission;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
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