using System.CommandLine;

namespace KeepersCompound.Lightmapper;

class Program
{
    private static void Main(string[] args)
    {
        var installPathArg = new Argument<string>(
            "installPath",
            "The path to the root Thief installation.");
        var campaignNameArg = new Argument<string>(
            "campaignName",
            "The folder name of the fan mission. For OMs this is blank.");
        var missionNameArg = new Argument<string>(
            "missionName",
            "The name of the mission file including extension.");
        var outputFileOption = new Option<string>(
            ["-o", "--output"],
            () => "kc_lit",
            "Name of output file excluding extension.");
        var multiSamplingOption = new Option<bool>(
            "--multiSampling",
            () => false,
            "Enables multi-sampled shadows. Higher quality but slower.");
        
        var rootCommand = new RootCommand("Compute lightmaps for a NewDark .MIS/.COW");
        rootCommand.AddArgument(installPathArg);
        rootCommand.AddArgument(campaignNameArg);
        rootCommand.AddArgument(missionNameArg);
        rootCommand.AddOption(outputFileOption);
        rootCommand.AddOption(multiSamplingOption);
        rootCommand.SetHandler((installPath, campaignName, missionName, outputFile, multiSampling) =>
        {
            Timing.Reset();

            var lightMapper = new LightMapper(installPath, campaignName, missionName);
            lightMapper.Light(multiSampling);
            lightMapper.Save(outputFile);

            Timing.LogAll();
        }, installPathArg, campaignNameArg, missionNameArg, outputFileOption, multiSamplingOption);

        rootCommand.Invoke(args);
    }
}