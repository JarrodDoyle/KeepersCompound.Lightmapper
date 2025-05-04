using System.Numerics;
using DotMake.CommandLine;
using KeepersCompound.LGS;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

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
            [CliArgument(Description = "The path to the root Thief installation.")]
            public required string InstallPath { get; set; }

            [CliArgument(Description = "The folder name of the fan mission. For OMs this is blank.")]
            public required string CampaignName { get; set; }

            [CliArgument(Description = "The name of the model")]
            public required string ModelName { get; set; }

            public void Run()
            {
                var tmpDir = Directory.CreateTempSubdirectory("KCBin");
                var pathManager = new ResourcePathManager(tmpDir.FullName);
                if (pathManager.TryInit(InstallPath) && pathManager.GetCampaignNames().Contains(CampaignName))
                {
                    var campaign = pathManager.GetCampaign(CampaignName);
                    var modelPath = campaign.GetResourcePath(ResourceType.Object, ModelName);
                    if (modelPath != null)
                    {
                        var modelFile = new ModelFile(modelPath);
                        modelFile.ApplyJoints([0, 0, 0, 0, 0, 0]);

                        var material = new MaterialBuilder()
                            .WithDoubleSide(false)
                            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 0, 0, 1));

                        var mesh = new MeshBuilder<VertexPosition>("mesh");
                        var prim = mesh.UsePrimitive(material);

                        var polyVertices = new List<Vector3>();
                        foreach (var poly in modelFile.Polygons)
                        {
                            polyVertices.Clear();
                            polyVertices.EnsureCapacity(poly.VertexCount);
                            foreach (var idx in poly.VertexIndices)
                            {
                                polyVertices.Add(modelFile.Vertices[idx]);
                            }

                            for (var i = 1; i < poly.VertexCount - 1; i++)
                            {
                                var v0 = polyVertices[0];
                                var v1 = polyVertices[i];
                                var v2 = polyVertices[i + 1];
                                prim.AddTriangle(
                                    new VertexPosition(v0.X, v0.Z, -v0.Y),
                                    new VertexPosition(v1.X, v1.Z, -v1.Y),
                                    new VertexPosition(v2.X, v2.Z, -v2.Y));
                            }
                        }

                        var scene = new SceneBuilder();
                        scene.AddRigidMesh(mesh, AffineTransform.Identity);
                        scene.ToGltf2().SaveGLB("./EXPORTS/test.glb");
                    }
                }
            }
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