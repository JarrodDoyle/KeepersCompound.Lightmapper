using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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

    public void Close()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopApp)
        {
            desktopApp.Shutdown();
        }
    }
}