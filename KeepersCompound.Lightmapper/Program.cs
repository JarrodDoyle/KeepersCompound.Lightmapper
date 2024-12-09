using DotMake.CommandLine;

namespace KeepersCompound.Lightmapper;

internal static class Program
{
    public static async Task<int> Main(string[] args) =>
        await Cli.RunAsync<LightCommand>(args);
}

[CliCommand(Description = "Compute lightmaps for a NewDark .MIS/.COW")]
public class LightCommand
{
    [CliArgument(Description = "The path to the root Thief installation.")]
    public required string InstallPath { get; set; }
    
    [CliArgument(Description = "The folder name of the fan mission. For OMs this is blank.")]
    public required string CampaignName { get; set; }
    
    [CliArgument(Description = "The name of the mission file including extension.")]
    public required string MissionName { get; set; }
    
    [CliOption(Description = "Name of output file excluding extension.")]
    public string OutputName { get; set; } = "kc_lit";

    public void Run()
    {
        Timing.Reset();
             
        var lightMapper = new LightMapper(InstallPath, CampaignName, MissionName);
        lightMapper.Light();
        lightMapper.Save(OutputName);
         
        Timing.LogAll();
    }
}