using Avalonia;

namespace KCLight.Gui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public string InstallPath { get; set; } = "";
    public string CampaignName { get; set; } = "";
    public string MissionName { get; set; } = "";
    public string OutputName { get; set; } = "kc_lit";
    public bool FastPvs { get; set; }
}