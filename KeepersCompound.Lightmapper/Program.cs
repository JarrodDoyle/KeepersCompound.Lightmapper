using CliFx;
using CliFx.Attributes;
using IConsole = CliFx.Infrastructure.IConsole;

namespace KeepersCompound.Lightmapper;

internal static class Program
{
    public static async Task<int> Main() =>
        await new CliApplicationBuilder().AddCommandsFromThisAssembly().Build().RunAsync();
}

[Command(Description = "Compute lightmaps for a NewDark .MIS/.COW")]
public class LightCommand : ICommand
{
    [CommandParameter(0, Description = "The path to the root Thief installation.")]
    public required string InstallPath { get; init; }
    [CommandParameter(1, Description = "The folder name of the fan mission. For OMs this is blank.")]
    public required string CampaignName { get; init; }
    [CommandParameter(2, Description = "The name of the mission file including extension.")]
    public required string MissionName { get; init; }
    [CommandOption("output", 'o', Description = "Name of output file excluding extension.")]
    public string OutputName { get; init; } = "kc_lit";

    public ValueTask ExecuteAsync(IConsole console)
    {
        Timing.Reset();
            
        var lightMapper = new LightMapper(InstallPath, CampaignName, MissionName);
        lightMapper.Light();
        lightMapper.Save(OutputName);
            
        Timing.LogAll();
        return default;
    }
}