using DotMake.CommandLine;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace KeepersCompound.Lightmapper;

internal static class Program
{
    private static void ConfigureLogger()
    {
        const string outputTemplate = "{Timestamp:HH:mm:ss.fff} [{Level}] {Message:lj}{NewLine}{Exception}";
        var logPath = $"{AppDomain.CurrentDomain.BaseDirectory}/logs/{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var config = new LoggerConfiguration();
#if DEBUG
        config.MinimumLevel.Debug();
#endif
        
        Log.Logger = config
            .WriteTo.Console(theme: AnsiConsoleTheme.Sixteen, outputTemplate: outputTemplate)
            .WriteTo.File(logPath, outputTemplate: outputTemplate)
            .CreateLogger();
    }
    
    public static async Task<int> Main(string[] args)
    {
        ConfigureLogger();
        return await Cli.RunAsync<LightCommand>(args);
    }
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
    
    [CliOption(Description = "Use a fast PVS calculation with looser cell light indices.")]
    public bool FastPvs { get; set; } = false;
    
    [CliOption(Description = "Name of output file excluding extension.")]
    public string OutputName { get; set; } = "kc_lit";

    public void Run()
    {
        Timing.Reset();
             
        var lightMapper = new LightMapper(InstallPath, CampaignName, MissionName);
        lightMapper.Light(FastPvs);
        lightMapper.Save(OutputName);
         
        Timing.LogAll();
    }
}