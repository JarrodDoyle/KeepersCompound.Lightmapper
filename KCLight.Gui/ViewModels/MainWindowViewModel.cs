using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepersCompound.Lighting;

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

    public bool FastPvs { get; set; }
    public bool CanRun => Directory.Exists(InstallPath) && CampaignName != "" && MissionName != "" && OutputName != "";

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var outputName = OutputName;
        await Task.Run(() =>
        {
            var lightMapper = new LightMapper(InstallPath, CampaignName, MissionName);
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
            AllowMultiple = false
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