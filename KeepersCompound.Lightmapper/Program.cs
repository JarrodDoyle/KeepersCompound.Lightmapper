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
                        color = HsbToRgb(360 * sz.Y, sz.Z, Math.Min(sz.X, 255.0f)),
                        radius = float.MaxValue
                    });
                }
            }

            // TODO: object lights
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
    }

    // Expects Hue to be 0-360, saturation 0-1, brightness 0-255
    // https://en.wikipedia.org/wiki/HSL_and_HSV#HSV_to_RGB
    private static Vector3 HsbToRgb(float hue, float saturation, float brightness)
    {
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
                    // Console.WriteLine($"Cell: {cellIdx}, Poly: {polyIdx}, V: {j}, Vert: {vertex}");
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
            for (int polyIdx = 0; polyIdx < maxPolyIdx; polyIdx++)
            {
                var poly = cell.Polys[polyIdx];
                var plane = cell.Planes[poly.PlaneId];
                var renderPoly = cell.RenderPolys[polyIdx];
                var info = cell.LightList[polyIdx];
                var lightmap = cell.Lightmaps[polyIdx];

                // Clear existing lightmap data
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

                foreach (var light in lights)
                {
                    Console.WriteLine("Doing a light...");
                    // Check if plane normal is facing towards the light
                    var direction = renderPoly.Center - light.position;
                    Console.WriteLine($"Light Pos: {light.position}, poly center: {renderPoly.Center}");
                    Console.WriteLine($"Dir: {direction}");
                    if (Vector3.Dot(plane.Normal, direction) < 0)
                    {
                        // Cast from the light to the center (later each pixel)
                        var hit = scene.Trace(new Ray
                        {
                            Origin = light.position,
                            Direction = Vector3.Normalize(direction)
                        });

                        // cheeky epsilon
                        var goodHit = hit && Math.Abs(hit.Distance - direction.Length()) < 0.001;
                        Console.WriteLine($"Did we hit? {goodHit}");
                        Console.WriteLine($"Distance: {hit.Distance}, target dist: {direction.Length()}");
                        Console.WriteLine($"Pos: {hit.Position}, Target Pos: {renderPoly.Center}");

                        // Iterate all pixels
                        var color = goodHit ? light.color : ambientLight;
                        for (var y = 0; y < lightmap.Height; y++)
                        {
                            for (var x = 0; x < lightmap.Width; x++)
                            {
                                lightmap.AddLight(0, x, y, (byte)color.X, (byte)color.Y, (byte)color.Z);
                            }
                        }
                    }
                }


                // // TODO: Get world position of each pixel?

                // var poly = cell.Polys[polyIdx];

                // poly.
            }
        }
    }
}