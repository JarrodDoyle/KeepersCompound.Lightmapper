using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using KeepersCompound.Lighting;

namespace KCLight.Gui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public string InstallPath { get; set; } = "";
    public string CampaignName { get; set; } = "";
    public string MissionName { get; set; } = "";
    public string OutputName { get; set; } = "kc_lit";
    public bool FastPvs { get; set; }
    
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