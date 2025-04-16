using System.Diagnostics;
using Serilog;

namespace KeepersCompound.Lighting;

public static class Timing
{
    private static readonly Dictionary<string, TimeSpan> Stages = new();

    public static void Reset()
    {
        Stages.Clear();
    }

    public static void TimeStage(string stagename, Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        watch.Stop();
        AddOrIncrement(stagename, watch.Elapsed);
    }

    public static T TimeStage<T>(string stagename, Func<T> action)
    {
        var watch = Stopwatch.StartNew();
        var value = action();
        watch.Stop();
        AddOrIncrement(stagename, watch.Elapsed);
        return value;
    }

    public static void LogAll()
    {
        foreach (var (stagename, time) in Stages)
        {
            Log.Information("Timing {StageName}: {Time:g}", stagename, time);
        }
    }

    private static void AddOrIncrement(string stagename, TimeSpan elapsed)
    {
        if (Stages.TryGetValue(stagename, out var time))
        {
            elapsed += time;
        }

        Stages[stagename] = elapsed;
    }
}