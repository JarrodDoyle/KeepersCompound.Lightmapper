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

                        var material = new MaterialBuilder()
                            .WithDoubleSide(false)
                            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 0, 0, 1));

                        var objCount = modelFile.Objects.Length;
                        var meshes = new MeshBuilder<VertexPosition>[objCount];
                        var nodes = new NodeBuilder[objCount];
                        for (var i = 0; i < objCount; i++)
                        {
                            var subObject = modelFile.Objects[i];

                            var mesh = new MeshBuilder<VertexPosition>(subObject.Name);
                            var prim = mesh.UsePrimitive(material);
                            foreach (var poly in modelFile.Polygons)
                            {
                                // Discards any polys that don't belong to this object
                                var v0Index = poly.VertexIndices[0];
                                if (v0Index < subObject.PointIdx ||
                                    v0Index >= subObject.PointIdx + subObject.PointCount)
                                {
                                    continue;
                                }

                                for (var j = 1; j < poly.VertexCount - 1; j++)
                                {
                                    var v0 = modelFile.Vertices[poly.VertexIndices[0]];
                                    var v1 = modelFile.Vertices[poly.VertexIndices[j]];
                                    var v2 = modelFile.Vertices[poly.VertexIndices[j + 1]];
                                    prim.AddTriangle(
                                        new VertexPosition(v0.X, v0.Y, v0.Z),
                                        new VertexPosition(v1.X, v1.Y, v1.Z),
                                        new VertexPosition(v2.X, v2.Y, v2.Z));
                                }
                            }

                            var transform = subObject.Joint == -1
                                ? AffineTransform.Identity
                                : AffineTransform.CreateDecomposed(subObject.Transform);
                            var node = new NodeBuilder(subObject.Name);
                            node.SetLocalTransform(transform, false);

                            meshes[i] = mesh;
                            nodes[i] = node;
                        }
                        
                        // Build node hierarchy
                        for (var i = 0; i < objCount; i++)
                        {
                            var subObject = modelFile.Objects[i];
                            var childIdx = subObject.Child;
                            while (childIdx != -1)
                            {
                                nodes[i].AddNode(nodes[childIdx]);
                                childIdx = modelFile.Objects[childIdx].Next;
                            }
                        }

                        var scene = new SceneBuilder();
                        for (var i = 0; i < objCount; i++)
                        {
                            scene.AddRigidMesh(meshes[i], nodes[i]);
                        }
                        
                        // GLTF uses different forward/right/up axes than Dark, but fortunately it's just a simple rotation
                        scene.ApplyBasisTransform(Matrix4x4.CreateRotationX(float.DegreesToRadians(-90)));
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