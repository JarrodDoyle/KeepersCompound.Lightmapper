using DotMake.CommandLine;
using Serilog;

namespace KCBin;

internal static class Program
{
    private static void ConfigureLogger()
    {
        const string outputTemplate = "{Timestamp:HH:mm:ss.fff} [{Level}] {Message:lj}{NewLine}{Exception}";
        var config = new LoggerConfiguration();

        Log.Logger = config
            .WriteTo.Console(theme: AnsiConsoleTheme.Sixteen, outputTemplate: outputTemplate)
            .CreateLogger();
    }

    public static void Main(string[] args)
    {
        ConfigureLogger();

        Cli.Run<RootCommand>(args);
    }
}

[CliCommand(Description = "Work with NewDark .BIN files.")]
public class RootCommand
{
    [CliCommand(Description = "Model file handling")]
    public class ModelCommand
    {
        [CliCommand(Description = "Dump information about a model")]
        public class DumpCommand
        {
        }

        [CliCommand(Description = "Import a model")]
        public class ImportCommand
        {
        }

        [CliCommand(Description = "Export a model")]
        public class ExportCommand
        {
        }
    }

    [CliCommand(Description = "Mesh file handling")]
    public class MeshCommand
    {
        [CliCommand(Description = "Dump information about a mesh")]
        public class DumpCommand
        {
        }

        [CliCommand(Description = "Import a mesh")]
        public class ImportCommand
        {
        }

        [CliCommand(Description = "Export a mesh")]
        public class ExportCommand
        {
        }
    }
}