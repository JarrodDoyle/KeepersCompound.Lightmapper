using System.Numerics;
using DotMake.CommandLine;
using KeepersCompound.LGS;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;

namespace KCBin;

using VERTEX = VertexBuilder<VertexPosition, VertexTexture1, VertexEmpty>;

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

                        var defaultMaterial = new MaterialBuilder()
                            .WithDoubleSide(false)
                            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 1, 1, 1));

                        var materials = new Dictionary<int, MaterialBuilder>();
                        foreach (var rawMaterial in modelFile.Materials)
                        {
                            var slot = rawMaterial.Slot;

                            if (rawMaterial.Type == 0)
                            {
                                var convertedName = ResourcePathManager.ConvertSeparator(rawMaterial.Name);
                                var resName = Path.GetFileNameWithoutExtension(convertedName);
                                var path = campaign.GetResourcePath(ResourceType.ObjectTexture, resName);
                                if (path == null)
                                {
                                    Log.Warning("Failed to find model texture, adding default material: {Name}, {Slot}",
                                        resName, slot);
                                    materials.Add(slot, defaultMaterial);
                                }
                                else
                                {
                                    var material = new MaterialBuilder()
                                        .WithDoubleSide(false)
                                        .WithBaseColor(ImageBuilder.From(new MemoryImage(path), resName));
                                    Log.Information("Adding texture material: {Name}, {Slot}", resName, slot);
                                    materials.Add(slot, material);
                                }
                            }
                            else
                            {
                                var b = rawMaterial.Handle & 0xff;
                                var g = (rawMaterial.Handle >> 8) & 0xff;
                                var r = (rawMaterial.Handle >> 16) & 0xff;
                                var colour = new Vector4(r, g, b, 255.0f) / 255.0f;
                                var material = new MaterialBuilder()
                                    .WithDoubleSide(false)
                                    .WithBaseColor(colour);
                                Log.Information("Adding colour material: {Colour}, {Slot}", colour, slot);
                                materials.Add(slot, material);
                            }
                        }

                        var objCount = modelFile.Objects.Length;
                        var meshes = new MeshBuilder<VertexPosition, VertexTexture1>[objCount];
                        var nodes = new NodeBuilder[objCount];
                        for (var i = 0; i < objCount; i++)
                        {
                            var subObject = modelFile.Objects[i];

                            var mesh = new MeshBuilder<VertexPosition, VertexTexture1>(subObject.Name);
                            var matPolyMap = new Dictionary<int, List<int>>();

                            var polyCount = modelFile.Polygons.Length;
                            for (var j = 0; j < polyCount; j++)
                            {
                                var poly = modelFile.Polygons[j];

                                // Discards any polys that don't belong to this object
                                var startIdx = poly.VertexIndices[0];
                                if (startIdx < subObject.PointIdx ||
                                    startIdx >= subObject.PointIdx + subObject.PointCount)
                                {
                                    continue;
                                }

                                if (matPolyMap.ContainsKey(poly.Data))
                                {
                                    matPolyMap[poly.Data].Add(j);
                                }
                                else
                                {
                                    matPolyMap[poly.Data] = [j];
                                }
                            }

                            foreach (var (materialIdx, polyIdxs) in matPolyMap)
                            {
                                var prim = mesh.UsePrimitive(materials[materialIdx]);
                                foreach (var polyIdx in polyIdxs)
                                {
                                    var poly = modelFile.Polygons[polyIdx];
                                    for (var j = 1; j < poly.VertexCount - 1; j++)
                                    {
                                        if (j < poly.UvIndices.Length)
                                        {
                                            prim.AddTriangle(
                                                new VERTEX(
                                                    modelFile.Vertices[poly.VertexIndices[0]],
                                                    modelFile.Uvs[poly.UvIndices[0]]),
                                                new VERTEX(
                                                    modelFile.Vertices[poly.VertexIndices[j + 1]],
                                                    modelFile.Uvs[poly.UvIndices[j + 1]]),
                                                new VERTEX(
                                                    modelFile.Vertices[poly.VertexIndices[j]],
                                                    modelFile.Uvs[poly.UvIndices[j]]));
                                        }
                                        else
                                        {
                                            prim.AddTriangle(
                                                new VERTEX(modelFile.Vertices[poly.VertexIndices[0]], Vector2.Zero),
                                                new VERTEX(modelFile.Vertices[poly.VertexIndices[j + 1]], Vector2.Zero),
                                                new VERTEX(modelFile.Vertices[poly.VertexIndices[j]], Vector2.Zero));
                                        }
                                    }
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