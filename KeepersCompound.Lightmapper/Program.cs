using System.Numerics;
using KeepersCompound.LGS.Database;
using KeepersCompound.LGS.Database.Chunks;
using TinyEmbree;

namespace KeepersCompound.Lightmapper;

class Program
{
    // Super simple for now
    private record Light
    {
        public Vector3 position;
        public Vector3 color;
        public float radius;
    }

    static void Main(string[] args)
    {
        var misPath = "/stuff/Games/thief/drive_c/GOG Games/TG ND 1.27 (MAPPING)/FMs/JAYRUDE_Tests/lm_test.cow";
        var mis = new DbFile(misPath);
        var hierarchy = BuildHierarchy(misPath, mis);

        // Get list of brush lights, and object lights (ignore anim lights for now)
        var lights = new List<Light>();
        if (mis.Chunks.TryGetValue("BRLIST", out var brListRaw))
        {
            var brList = (BrList)brListRaw;
            foreach (var brush in brList.Brushes)
            {
                if (brush.media == BrList.Brush.Media.Light)
                {
                    var sz = brush.size;
                    lights.Add(new Light
                    {
                        position = brush.position,
                        color = HsbToRgb(sz.Y, sz.Z, Math.Min(sz.X, 255.0f)),
                        radius = float.MaxValue
                    });
                }
                else if (brush.media == BrList.Brush.Media.Object)
                {
                    var id = (int)brush.brushInfo;
                    var light = hierarchy.GetProperty<PropLight>(id, "P$Light");
                    var lightColor = hierarchy.GetProperty<PropLightColor>(id, "P$LightColo");

                    if (light != null)
                    {
                        lightColor ??= new PropLightColor { Hue = 0, Saturation = 0 };
                        lights.Add(new Light
                        {
                            position = brush.position,
                            color = HsbToRgb(lightColor.Hue, lightColor.Saturation, light.Brightness),
                            radius = light.Radius,
                        });
                    }
                    else
                    {
                        Console.WriteLine($"no light prop apparently");

                    }
                }
            }
        }

        // Build embree mesh
        if (!mis.Chunks.TryGetValue("WREXT", out var wrRaw))
            return;
        var worldRep = (WorldRep)wrRaw;
        var scene = new Raytracer();
        scene.AddMesh(BuildWrMesh(worldRep));
        scene.CommitScene();

        // For each lightmap pixel cast against all the brush and object lights
        if (!mis.Chunks.TryGetValue("RENDPARAMS", out var rendParamsRaw))
            return;
        var ambient = ((RendParams)rendParamsRaw).ambientLight * 255;
        CastScene(scene, worldRep, [.. lights], ambient);

        var dir = Path.GetDirectoryName(misPath);
        var filename = Path.GetFileNameWithoutExtension(misPath);
        mis.Save(Path.Join(dir, $"{filename}-lit.cow"));

        Console.WriteLine($"Lit {lights.Count} light");
    }

    // Expects Hue and Saturation are 0-1, Brightness 0-255
    // https://en.wikipedia.org/wiki/HSL_and_HSV#HSV_to_RGB
    private static Vector3 HsbToRgb(float hue, float saturation, float brightness)
    {
        hue *= 360;
        var hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        var f = hue / 60 - Math.Floor(hue / 60);

        var v = Convert.ToInt32(brightness);
        var p = Convert.ToInt32(brightness * (1 - saturation));
        var q = Convert.ToInt32(brightness * (1 - f * saturation));
        var t = Convert.ToInt32(brightness * (1 - (1 - f) * saturation));

        return hi switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }

    private static ObjectHierarchy BuildHierarchy(string misPath, DbFile misFile)
    {
        ObjectHierarchy objHierarchy;
        if (misFile.Chunks.TryGetValue("GAM_FILE", out var gamFileChunk))
        {
            var dir = Path.GetDirectoryName(misPath);
            var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
            var name = ((GamFile)gamFileChunk).fileName;
            var paths = Directory.GetFiles(dir!, name, options);
            if (paths.Length > 0)
            {
                objHierarchy = new ObjectHierarchy(misFile, new DbFile(paths[0]));
            }
            else
            {
                objHierarchy = new ObjectHierarchy(misFile);
            }
        }
        else
        {
            objHierarchy = new ObjectHierarchy(misFile);
        }
        return objHierarchy;
    }

    private static TriangleMesh BuildWrMesh(WorldRep worldRep)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();

        var cells = worldRep.Cells;
        for (var cellIdx = 0; cellIdx < cells.Length; cellIdx++)
        {
            var cell = cells[cellIdx];
            var numPolys = cell.PolyCount;
            var numRenderPolys = cell.RenderPolyCount;
            var numPortalPolys = cell.PortalPolyCount;

            // There's nothing to render
            if (numRenderPolys == 0 || numPortalPolys >= numPolys)
            {
                continue;
            }

            var maxPolyIdx = Math.Min(numRenderPolys, numPolys - numPortalPolys);
            var cellIdxOffset = 0;
            for (int polyIdx = 0; polyIdx < maxPolyIdx; polyIdx++)
            {
                var poly = cell.Polys[polyIdx];

                var meshIndexOffset = vertices.Count;
                var numPolyVertices = poly.VertexCount;
                for (var j = 0; j < numPolyVertices; j++)
                {
                    var vertex = cell.Vertices[cell.Indices[cellIdxOffset + j]];
                    vertices.Add(vertex);
                }

                for (int j = 1; j < numPolyVertices - 1; j++)
                {
                    indices.Add(meshIndexOffset);
                    indices.Add(meshIndexOffset + j);
                    indices.Add(meshIndexOffset + j + 1);
                }

                cellIdxOffset += cell.Polys[polyIdx].VertexCount;
            }
        }

        return new TriangleMesh([.. vertices], [.. indices]);
    }

    private static void CastScene(Raytracer scene, WorldRep wr, Light[] lights, Vector3 ambientLight)
    {
        var cells = wr.Cells;
        for (var cellIdx = 0; cellIdx < cells.Length; cellIdx++)
        {
            Console.Write($"\rLighting cell... {cellIdx + 1}/{cells.Length}\n");

            var cell = cells[cellIdx];
            var numPolys = cell.PolyCount;
            var numRenderPolys = cell.RenderPolyCount;
            var numPortalPolys = cell.PortalPolyCount;

            // There's nothing to render
            // Portal polys can be render polys (e.g. water) but we're ignoring them for now
            if (numRenderPolys == 0 || numPortalPolys >= numPolys)
            {
                continue;
            }

            var maxPolyIdx = Math.Min(numRenderPolys, numPolys - numPortalPolys);
            var cellIdxOffset = 0;
            for (int polyIdx = 0; polyIdx < maxPolyIdx; polyIdx++)
            {
                var poly = cell.Polys[polyIdx];
                var plane = cell.Planes[poly.PlaneId];
                var renderPoly = cell.RenderPolys[polyIdx];
                var info = cell.LightList[polyIdx];
                var lightmap = cell.Lightmaps[polyIdx];

                ResetLightmap(ambientLight, lightmap);

                // Get world position of lightmap (0, 0)
                var baseU = (4.0f / info.Width) * (renderPoly.TextureBases.Item1 + (0.5f - info.Bases.Item1) * 0.25f);
                var baseV = (4.0f / info.Height) * (renderPoly.TextureBases.Item2 + (0.5f - info.Bases.Item2) * 0.25f);
                var topLeft = cell.Vertices[cell.Indices[cellIdxOffset]];
                topLeft -= baseU * (info.Width * 0.25f) * renderPoly.TextureVectors.Item1;
                topLeft -= baseV * (info.Height * 0.25f) * renderPoly.TextureVectors.Item2;

                foreach (var light in lights)
                {
                    // Check if plane normal is facing towards the light
                    // If it's not then we're never going to be (directly) lit by this
                    // light.
                    var centerDirection = renderPoly.Center - light.position;
                    if (Vector3.Dot(plane.Normal, centerDirection) >= 0)
                    {
                        continue;
                    }

                    for (var y = 0; y < lightmap.Height; y++)
                    {
                        for (var x = 0; x < lightmap.Width; x++)
                        {
                            var pos = topLeft;
                            pos += x * 0.25f * renderPoly.TextureVectors.Item1;
                            pos += y * 0.25f * renderPoly.TextureVectors.Item2;

                            // Cast from the light to the center (later each pixel)
                            var direction = pos - light.position;
                            var hitResult = scene.Trace(new Ray
                            {
                                Origin = light.position,
                                Direction = Vector3.Normalize(direction),
                            });

                            // cheeky epsilon
                            var hit = hitResult && Math.Abs(hitResult.Distance - direction.Length()) < 0.001;
                            if (hit)
                            {
                                lightmap.AddLight(0, x, y, (byte)light.color.X, (byte)light.color.Y, (byte)light.color.Z);
                            }
                        }
                    }
                }

                cellIdxOffset += poly.VertexCount;
            }
        }

        Console.Write("\n");
    }

    private static void ResetLightmap(Vector3 ambientLight, WorldRep.Cell.Lightmap lightmap)
    {
        for (var i = 0; i < lightmap.Pixels.Length; i++)
        {
            lightmap.Pixels[i] = 0;
        }

        for (var y = 0; y < lightmap.Height; y++)
        {
            for (var x = 0; x < lightmap.Width; x++)
            {
                lightmap.AddLight(0, x, y, (byte)ambientLight.X, (byte)ambientLight.Y, (byte)ambientLight.Z);
            }
        }
    }
}