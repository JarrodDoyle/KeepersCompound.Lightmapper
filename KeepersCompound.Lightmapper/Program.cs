using System.Diagnostics;
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
        public float r2;
    }

    static void Main(string[] args)
    {
        Timing.Reset();

        var misPath = "/stuff/Games/thief/drive_c/GOG Games/TG ND 1.27 (MAPPING)/FMs/JAYRUDE_Tests/lm_test.cow";
        misPath = "/stuff/Games/thief/drive_c/GOG Games/TG ND 1.27 (MAPPING)/FMs/AtdV/miss20.mis";
        misPath = "/stuff/Games/thief/drive_c/GOG Games/TG ND 1.27 (MAPPING)/FMs/TDP20AC_a_burrick_in_a_room/miss20.mis";
        Timing.TimeStage("Total", () => LightmapMission(misPath));

        Timing.LogAll();
    }

    private static void LightmapMission(string misPath)
    {
        var mis = Timing.TimeStage("Parse DB", () => new DbFile(misPath));
        var hierarchy = Timing.TimeStage("Build Hierarchy", () => BuildHierarchy(misPath, mis));

        var lights = Timing.TimeStage("Gather Lights", () => BuildLightList(mis, hierarchy));

        // Build embree mesh
        if (!mis.Chunks.TryGetValue("WREXT", out var wrRaw))
            return;
        var worldRep = (WorldRep)wrRaw;
        var scene = Timing.TimeStage("Build Scene", () =>
        {
            var rt = new Raytracer();
            rt.AddMesh(BuildWrMesh(worldRep));
            rt.CommitScene();
            return rt;
        });

        // For each lightmap pixel cast against all the brush and object lights
        if (!mis.Chunks.TryGetValue("RENDPARAMS", out var rendParamsRaw))
            return;
        var ambient = ((RendParams)rendParamsRaw).ambientLight * 255;
        Timing.TimeStage("Light", () => CastScene(scene, worldRep, [.. lights], ambient));

        var dir = Path.GetDirectoryName(misPath);
        var filename = Path.GetFileNameWithoutExtension(misPath);
        var savePath = Path.Join(dir, $"{filename}-lit.cow");
        Timing.TimeStage("Save DB", () => mis.Save(savePath));

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

    // Get list of brush lights, and object lights (ignore anim lights for now)
    private static List<Light> BuildLightList(DbFile mis, ObjectHierarchy hierarchy)
    {
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
                        radius = float.MaxValue,
                        r2 = float.MaxValue
                    });
                }
                else if (brush.media == BrList.Brush.Media.Object)
                {
                    var id = (int)brush.brushInfo;
                    var propLight = hierarchy.GetProperty<PropLight>(id, "P$Light");
                    var propLightColor = hierarchy.GetProperty<PropLightColor>(id, "P$LightColo");

                    if (propLight != null)
                    {
                        propLightColor ??= new PropLightColor { Hue = 0, Saturation = 0 };

                        var light = new Light
                        {
                            position = brush.position,
                            color = HsbToRgb(propLightColor.Hue, propLightColor.Saturation, propLight.Brightness),
                            radius = propLight.Radius,
                            r2 = propLight.Radius * propLight.Radius,
                        };

                        if (propLight.Radius == 0)
                        {
                            light.radius = float.MaxValue;
                            light.r2 = float.MaxValue;
                        }

                        lights.Add(light);
                    }
                }
            }
        }

        return lights;
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
        var hdr = wr.DataHeader.LightmapFormat == 2;

        var cells = wr.Cells;
        for (var cellIdx = 0; cellIdx < cells.Length; cellIdx++)
        {
            Console.Write($"\rLighting cell... {cellIdx + 1}/{cells.Length}");

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

                // Get world position of lightmap (0, 0) (+0.5 so we cast from the center of a pixel)
                var topLeft = cell.Vertices[cell.Indices[cellIdxOffset]];
                topLeft -= renderPoly.TextureVectors.Item1 * (renderPoly.TextureBases.Item1 - info.Bases.Item1 * 0.25f);
                topLeft -= renderPoly.TextureVectors.Item2 * (renderPoly.TextureBases.Item2 - info.Bases.Item2 * 0.25f);

                var xDir = 0.25f * lightmap.Width * renderPoly.TextureVectors.Item1;
                var yDir = 0.25f * lightmap.Height * renderPoly.TextureVectors.Item2;
                var aabb = new MathUtils.Aabb([
                    topLeft,
                    topLeft + xDir,
                    topLeft + yDir,
                    topLeft + xDir + yDir,
                ]);

                // Used for clipping points to poly
                var vs = new Vector3[poly.VertexCount];
                for (var i = 0; i < poly.VertexCount; i++)
                {
                    vs[i] = cell.Vertices[cell.Indices[cellIdxOffset + i]];
                }

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

                    // If there aren't *any* points on the plane that are in range of the light
                    // then none of the lightmap points will be so we can discard.
                    // The more compact a map is the less effective this is
                    var planeDist = MathUtils.DistanceFromPlane(plane, light.position);
                    if (planeDist > light.radius)
                    {
                        continue;
                    }

                    // If the poly of the lightmap doesn't intersect the light radius then
                    // none of the lightmap points will so we can discard.
                    if (!MathUtils.Intersects(new MathUtils.Sphere(light.position, light.radius), aabb))
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

                            pos = MathUtils.ClipPointToPoly3d(pos, vs, plane);

                            // If we're out of range there's no point casting a ray
                            // There's probably a better way to discard the entire lightmap
                            // if we're massively out of range
                            var direction = pos - light.position;
                            if (direction.LengthSquared() > light.r2)
                            {
                                continue;
                            }

                            // We cast from the light to the pixel because the light has
                            // no mesh in the scene to hit
                            var hitResult = scene.Trace(new Ray
                            {
                                Origin = light.position,
                                Direction = Vector3.Normalize(direction),
                            });

                            // cheeky epsilon
                            // TODO: Some pixels aren't hitting and I'm not sure why
                            var hit = hitResult && Math.Abs(hitResult.Distance - direction.Length()) < 0.001;
                            if (hit)
                            {
                                // Calculate light strength at a given point. As far as I can tell
                                // this is exact to Dark (I'm a genius??). It's just an inverse distance
                                // falloff with diffuse angle, except we have to scale the length.
                                var dir = light.position - pos;
                                var angle = Vector3.Dot(Vector3.Normalize(dir), plane.Normal);
                                var len = dir.Length();
                                var slen = len / 4.0f;
                                var strength = (angle + 1.0f) / slen;
                                if (hdr)
                                {
                                    strength /= 2;
                                }

                                // We need to make sure we don't go over (255, 255, 255).
                                // If we just do Max(color, (255, 255, 255)) then we change
                                // the hue/saturation of coloured lights. Got to make sure we
                                // maintain the colour ratios.
                                var c = light.color * strength;
                                var ratio = 0.0f;
                                foreach (var e in new float[] { c.X, c.Y, c.Z })
                                {
                                    ratio = Math.Max(ratio, e / 255.0f);
                                }

                                if (ratio > 1.0f)
                                {
                                    c /= ratio;
                                }

                                lightmap.AddLight(0, x, y, (byte)c.X, (byte)c.Y, (byte)c.Z);
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