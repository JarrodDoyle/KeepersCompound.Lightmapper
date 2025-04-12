using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using KeepersCompound.Lighting;

namespace KCLight.Gui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanRun))]
    private string _installPath = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanRun))]
    private string _campaignName = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanRun))]
    private string _missionName = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanRun))]
    private string _outputName = "kc_lit";

    public bool FastPvs { get; set; }
    public bool CanRun => Directory.Exists(InstallPath) && CampaignName != "" && MissionName != "" && OutputName != "";

    public bool Run()
    {
        if (InstallPath == "" || CampaignName == "" || MissionName == "" || OutputName == "")
        {
            return false;
        }

        var lightMapper = new LightMapper(InstallPath, CampaignName, MissionName);
        lightMapper.Light(FastPvs);
        lightMapper.Save(OutputName);
        return true;
    }

    public void Close()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopApp)
        {
            desktopApp.Shutdown();
        }
    }
}