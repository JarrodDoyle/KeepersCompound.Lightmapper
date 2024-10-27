using System.Numerics;
using KeepersCompound.LGS;
using KeepersCompound.LGS.Database;
using KeepersCompound.LGS.Database.Chunks;
using TinyEmbree;

namespace KeepersCompound.Lightmapper;

class Program
{
    static void Main(string[] args)
    {
        Timing.Reset();

        // TODO: Read this from args
        var installPath = "/stuff/Games/thief/drive_c/GOG Games/TG ND 1.27 (MAPPING)/";
        var campaignName = "JAYRUDE_Tests";
        var missionName = "lm_test.cow";

        // campaignName = "JAYRUDE_1MIL_Mages";
        // campaignName = "TDP20AC_a_burrick_in_a_room";
        // campaignName = "AtdV";
        // missionName = "miss20.mis";

        // Setup extract path
        var tmpDir = Directory.CreateTempSubdirectory("KCLightmapper");
        var resPathManager = new ResourcePathManager(tmpDir.FullName);
        resPathManager.Init(installPath);

        var campaign = resPathManager.GetCampaign(campaignName);
        var misPath = campaign.GetResourcePath(ResourceType.Mission, missionName);
        Timing.TimeStage("Total", () => LightmapMission(campaign, misPath));

        Timing.LogAll();
    }

    private static void LightmapMission(ResourcePathManager.CampaignResources campaign, string misPath)
    {
        var mis = Timing.TimeStage("Parse DB", () => new DbFile(misPath));
        var hierarchy = Timing.TimeStage("Build Hierarchy", () => BuildHierarchy(misPath, mis));

        // Build embree mesh
        if (!mis.TryGetChunk<WorldRep>("WREXT", out var worldRep))
            return;
        var scene = Timing.TimeStage("Build Scene", () =>
        {
            var rt = new Raytracer();
            rt.AddMesh(BuildWrMesh(worldRep));
            rt.CommitScene();
            return rt;
        });

        // For each lightmap pixel cast against all the brush and object lights
        if (!mis.TryGetChunk<RendParams>("RENDPARAMS", out var rendParams))
            return;
        var ambient = rendParams.ambientLight * 255;
        var lights = Timing.TimeStage("Gather Lights", () => BuildLightList(mis, hierarchy, campaign));
        Timing.TimeStage("Set Light Indices", () => SetCellLightIndices(worldRep, [.. lights]));
        Timing.TimeStage("Light", () => CastSceneParallel(scene, worldRep, [.. lights], ambient));
        Timing.TimeStage("Update Anim Mapping", () => SetAnimLightCellMaps(mis, worldRep, lights));

        var dir = Path.GetDirectoryName(misPath);
        var filename = Path.GetFileNameWithoutExtension(misPath);
        var savePath = Path.Join(dir, $"{filename}-lit.cow");
        Timing.TimeStage("Save DB", () => mis.Save(savePath));

        Console.WriteLine($"Lit {lights.Count} light");
    }

    private static void SetAnimLightCellMaps(
        DbFile mis,
        WorldRep worldRep,
        List<Light> lights)
    {
        // Now that we've set all the per-cell stuff we need to aggregate the cell mappings
        // We can't do this in parallel which is why it's being done afterwards rather than
        // as we go
        var map = new Dictionary<ushort, List<WorldRep.LightTable.AnimCellMap>>();
        for (ushort i = 0; i < worldRep.Cells.Length; i++)
        {
            var cell = worldRep.Cells[i];
            for (ushort j = 0; j < cell.AnimLightCount; j++)
            {
                var animLightIdx = cell.AnimLights[j];
                if (!map.TryGetValue(animLightIdx, out var value))
                {
                    value = [];
                    map[animLightIdx] = value;
                }
                value.Add(new WorldRep.LightTable.AnimCellMap
                {
                    CellIndex = i,
                    LightIndex = j,
                });
            }
        }

        if (!mis.TryGetChunk<PropertyChunk<PropAnimLight>>("P$AnimLight", out var animLightChunk))
        {
            return;
        }
        foreach (var (lightIdx, animCellMaps) in map)
        {
            // We need to update the object property so it knows its mapping range
            // TODO: Handle nulls
            var light = lights.Find(l => l.anim && l.lightTableIndex == lightIdx);
            var prop = animLightChunk.properties.Find(p => p.objectId == light.objId);
            prop.LightTableLightIndex = lightIdx;
            prop.LightTableMapIndex = (ushort)worldRep.LightingTable.AnimMapCount;
            prop.CellsReached = (ushort)animCellMaps.Count;

            worldRep.LightingTable.AnimCellMaps.AddRange(animCellMaps);
            worldRep.LightingTable.AnimMapCount += animCellMaps.Count;
        }
    }

    // Gather all the brush, object, and anim ligths. Resets the lighting table
    // TODO: Handle dynamic lights
    private static List<Light> BuildLightList(
        DbFile mis,
        ObjectHierarchy hierarchy,
        ResourcePathManager.CampaignResources campaign)
    {
        var lights = new List<Light>();

        // Get the chunks we need
        if (!mis.TryGetChunk<WorldRep>("WREXT", out var worldRep) ||
            !mis.TryGetChunk<BrList>("BRLIST", out var brList))
        {
            return lights;
        }

        worldRep.LightingTable.Reset();
        
        foreach (var brush in brList.Brushes)
        {
            switch (brush.media)
            {
                case BrList.Brush.Media.Light:
                    ProcessBrushLight(lights, worldRep.LightingTable, brush);
                    break;
                case BrList.Brush.Media.Object:
                    ProcessObjectLight(
                        lights,
                        hierarchy,
                        campaign,
                        worldRep.LightingTable,
                        brush);
                    break;
            }
        }
        
        return lights;
    }

    // TODO: Check if this works (brush is a record type)
    private static void ProcessBrushLight(List<Light> lights, WorldRep.LightTable lightTable, BrList.Brush brush)
    {
        // For some reason the light table index on brush lights is 1 indexed
        brush.brushInfo = (uint)lightTable.LightCount + 1;

        var sz = brush.size;
        var light = new Light
        {
            position = brush.position,
            color = Utils.HsbToRgb(sz.Y, sz.Z, Math.Min(sz.X, 255.0f)),
            radius = float.MaxValue,
            r2 = float.MaxValue,
            lightTableIndex = lightTable.LightCount,
        };
        
        lights.Add(light);
        lightTable.AddLight(new WorldRep.LightTable.LightData
        {
            Location = light.position,
            Direction = light.spotlightDir,
            Color = light.color / 32.0f, // TODO: This is based on light_scale config var
            InnerAngle = -1.0f,
            Radius = 0,
        });
    }

    private static void ProcessObjectLight(
        List<Light> lights,
        ObjectHierarchy hierarchy,
        ResourcePathManager.CampaignResources campaign,
        WorldRep.LightTable lightTable,
        BrList.Brush brush)
    {
        // TODO: Handle PropSpotlightAndAmbient
        var id = (int)brush.brushInfo;
        var propAnimLight = hierarchy.GetProperty<PropAnimLight>(id, "P$AnimLight", false);
        var propLight = hierarchy.GetProperty<PropLight>(id, "P$Light", false);
        var propLightColor = hierarchy.GetProperty<PropLightColor>(id, "P$LightColo");
        var propSpotlight = hierarchy.GetProperty<PropSpotlight>(id, "P$Spotlight");
        var propModelName = hierarchy.GetProperty<PropLabel>(id, "P$ModelName");

        propLightColor ??= new PropLightColor { Hue = 0, Saturation = 0 };

        var baseLight = new Light
        {
            position = brush.position,
            spotlightDir = -Vector3.UnitZ,
            spotlightInnerAngle = -1.0f,
        };

        if (propModelName != null)
        {
            var resName = $"{propModelName.value.ToLower()}.bin";
            var modelPath = campaign.GetResourcePath(ResourceType.Object, resName);
            if (modelPath != null)
            {
                // TODO: Handle failing to find model more gracefully
                var model = new ModelFile(modelPath);
                if (model.TryGetVhot(ModelFile.VhotId.LightPosition, out var vhot))
                {
                    baseLight.position += vhot.Position;
                }
                if (model.TryGetVhot(ModelFile.VhotId.LightDirection, out vhot))
                {
                    baseLight.spotlightDir = vhot.Position;
                }
            }

        }

        if (propSpotlight != null)
        {
            var rot = Matrix4x4.Identity;
            rot *= Matrix4x4.CreateRotationX(float.DegreesToRadians(brush.angle.X));
            rot *= Matrix4x4.CreateRotationY(float.DegreesToRadians(brush.angle.Y));
            rot *= Matrix4x4.CreateRotationZ(float.DegreesToRadians(brush.angle.Z));

            baseLight.spotlight = true;
            baseLight.spotlightDir = Vector3.Transform(baseLight.spotlightDir, rot);
            baseLight.spotlightInnerAngle = (float)Math.Cos(float.DegreesToRadians(propSpotlight.InnerAngle));
            baseLight.spotlightOuterAngle = (float)Math.Cos(float.DegreesToRadians(propSpotlight.OuterAngle));
        }

        if (propLight != null)
        {
            var light = new Light
            {
                position = baseLight.position + propLight.Offset,
                color = Utils.HsbToRgb(propLightColor.Hue, propLightColor.Saturation, propLight.Brightness),
                innerRadius = propLight.InnerRadius,
                radius = propLight.Radius,
                r2 = propLight.Radius * propLight.Radius,
                spotlight = baseLight.spotlight,
                spotlightDir = baseLight.spotlightDir,
                spotlightInnerAngle = baseLight.spotlightInnerAngle,
                spotlightOuterAngle = baseLight.spotlightOuterAngle,
                lightTableIndex = lightTable.LightCount,
            };
            if (propLight.Radius == 0)
            {
                light.radius = float.MaxValue;
                light.r2 = float.MaxValue;
            }

            lights.Add(light);
            lightTable.AddLight(light.ToLightData(32.0f));
        }

        if (propAnimLight != null)
        {
            var lightIndex = lightTable.LightCount;
            propAnimLight.LightTableLightIndex = (ushort)lightIndex;

            var light = new Light
            {
                position = baseLight.position + propAnimLight.Offset,
                color = Utils.HsbToRgb(propLightColor.Hue, propLightColor.Saturation, propAnimLight.MaxBrightness),
                innerRadius = propAnimLight.InnerRadius,
                radius = propAnimLight.Radius,
                r2 = propAnimLight.Radius * propAnimLight.Radius,
                spotlight = baseLight.spotlight,
                spotlightDir = baseLight.spotlightDir,
                spotlightInnerAngle = baseLight.spotlightInnerAngle,
                spotlightOuterAngle = baseLight.spotlightOuterAngle,
                anim = true,
                objId = id,
                lightTableIndex = propAnimLight.LightTableLightIndex,
            };
            if (propAnimLight.Radius == 0)
            {
                light.radius = float.MaxValue;
                light.r2 = float.MaxValue;
            }

            lights.Add(light);
            lightTable.AddLight(light.ToLightData(32.0f));
        }
    }

    private static ObjectHierarchy BuildHierarchy(string misPath, DbFile misFile)
    {
        ObjectHierarchy objHierarchy;
        if (misFile.TryGetChunk<GamFile>("GAM_FILE", out var gamFile))
        {
            var dir = Path.GetDirectoryName(misPath);
            var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
            var name = gamFile.fileName;
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
        foreach (var cell in cells)
        {
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
            for (var polyIdx = 0; polyIdx < maxPolyIdx; polyIdx++)
            {
                var poly = cell.Polys[polyIdx];

                var meshIndexOffset = vertices.Count;
                var numPolyVertices = poly.VertexCount;
                for (var j = 0; j < numPolyVertices; j++)
                {
                    var vertex = cell.Vertices[cell.Indices[cellIdxOffset + j]];
                    vertices.Add(vertex);
                }

                for (var j = 1; j < numPolyVertices - 1; j++)
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

    private static void CastSceneParallel(Raytracer scene, WorldRep wr, Light[] lights, Vector3 ambientLight)
    {
        var hdr = wr.DataHeader.LightmapFormat == 2;
        Parallel.ForEach(wr.Cells, cell =>
        {
            // Reset cell AnimLight palette
            cell.AnimLightCount = 0;
            cell.AnimLights.Clear();

            var numPolys = cell.PolyCount;
            var numRenderPolys = cell.RenderPolyCount;
            var numPortalPolys = cell.PortalPolyCount;

            // There's nothing to render
            // Portal polys can be render polys (e.g. water) but we're ignoring them for now
            if (numRenderPolys == 0 || numPortalPolys >= numPolys)
            {
                return;
            }

            var maxPolyIdx = Math.Min(numRenderPolys, numPolys - numPortalPolys);
            var cellIdxOffset = 0;
            for (var polyIdx = 0; polyIdx < maxPolyIdx; polyIdx++)
            {
                var poly = cell.Polys[polyIdx];
                var plane = cell.Planes[poly.PlaneId];
                var renderPoly = cell.RenderPolys[polyIdx];
                var info = cell.LightList[polyIdx];
                var lightmap = cell.Lightmaps[polyIdx];

                info.AnimLightBitmask = 0;
                lightmap.Reset(ambientLight, hdr);

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
                var planeMapper = new MathUtils.PlanePointMapper(plane.Normal, vs[0], vs[1]);
                var v2ds = planeMapper.MapTo2d(vs);

                foreach (var light in lights)
                {
                    var layer = 0;

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

                            // We need to clip the point to slightly inside of the poly
                            // to avoid three problems:
                            // 1. Darkened spots from lightmap pixels who's center is outside
                            //    of the polygon but is partially contained in the polygon
                            // 2. Darkened spots from linear filtering of points outside of the
                            //    polygon which have missed
                            // 3. Darkened spots where centers are on the exact edge of a poly
                            //    which can sometimes cause Embree to miss casts
                            var p2d = planeMapper.MapTo2d(pos);
                            p2d = MathUtils.ClipPointToPoly2d(p2d, v2ds);
                            pos = planeMapper.MapTo3d(p2d);

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
                            var hit = hitResult && Math.Abs(hitResult.Distance - direction.Length()) < MathUtils.Epsilon;
                            if (hit)
                            {
                                // If we're an anim light there's a lot of stuff we need to update
                                // Firstly we need to add the light to the cells anim light palette
                                // Secondly we need to set the appropriate bit of the lightmap's
                                // bitmask. Finally we need to check if the lightmap needs another layer
                                if (light.anim)
                                {
                                    // TODO: Don't recalculate this for every point lol
                                    var paletteIdx = cell.AnimLights.IndexOf((ushort)light.lightTableIndex);
                                    if (paletteIdx == -1)
                                    {
                                        paletteIdx = cell.AnimLightCount;
                                        cell.AnimLightCount++;
                                        cell.AnimLights.Add((ushort)light.lightTableIndex);
                                    }
                                    info.AnimLightBitmask |= 1u << paletteIdx;
                                    layer = paletteIdx + 1;
                                }
                                var strength = CalculateLightStrengthAtPoint(light, pos, plane);
                                lightmap.AddLight(layer, x, y, light.color, strength, hdr);
                            }
                        }
                    }
                }

                cellIdxOffset += poly.VertexCount;
            }
        });
    }

    private static void SetCellLightIndices(WorldRep wr, Light[] lights)
    {
        // TODO: Move this functionality to the LGS library
        // We set up light indices in separately from lighting because the actual
        // lighting phase takes a lot of shortcuts that we don't want
        Parallel.ForEach(wr.Cells, cell =>
        {
            cell.LightIndexCount = 0;
            cell.LightIndices.Clear();
            
            // The first element of the light indices array is used to store how many
            // actual lights are in the list. Which is just LightIndexCount - 1...
            // Odd choice I know
            cell.LightIndexCount++;
            cell.LightIndices.Add(0);

            // The OG lightmapper uses the cell traversal to work out all the cells that
            // are actually visited. We're a lot more coarse and just say if a cell is
            // in range then we potentially affect the lighting in the cell and add it 
            // to the list. Cells already contain their sphere bounds so we just use
            // that for now, but a tighter AABB is another option.
            var cellSphere = new MathUtils.Sphere(cell.SphereCenter, cell.SphereRadius);
            foreach (var light in lights)
            {
                if (MathUtils.Intersects(cellSphere, new MathUtils.Sphere(light.position, light.radius)))
                {
                    cell.LightIndexCount++;
                    cell.LightIndices.Add((ushort)light.lightTableIndex);
                    cell.LightIndices[0]++;
                }
            }
        });
    }

    private static float CalculateLightStrengthAtPoint(Light light, Vector3 point, Plane plane)
    {
        // Calculate light strength at a given point. As far as I can tell
        // this is exact to Dark (I'm a genius??). It's just an inverse distance
        // falloff with diffuse angle, except we have to scale the length.
        var dir = light.position - point;
        var angle = Vector3.Dot(Vector3.Normalize(dir), plane.Normal);
        var len = dir.Length();
        var slen = len / 4.0f;
        var strength = (angle + 1.0f) / slen;

        // Inner radius starts a linear falloff to 0 at the radius
        if (light.innerRadius != 0 && len > light.innerRadius)
        {
            strength *= (light.radius - len) / (light.radius - light.innerRadius);
        }

        // This is basically the same as how inner radius works. It just applies
        // a linear falloff to 0 between the inner angle and outer angle.
        if (light.spotlight)
        {
            var spotAngle = Vector3.Dot(-Vector3.Normalize(dir), light.spotlightDir);
            var inner = light.spotlightInnerAngle;
            var outer = light.spotlightOuterAngle;

            // In an improperly configured spotlight inner and outer angles might be the
            // same. So to avoid division by zero (and some clamping) we explicitly handle
            // some cases
            float spotlightMultiplier;
            if (spotAngle >= inner)
            {
                spotlightMultiplier = 1.0f;
            }
            else if (spotAngle <= outer)
            {
                spotlightMultiplier = 0.0f;
            }
            else
            {
                spotlightMultiplier = (spotAngle - outer) / (inner - outer);
            }

            strength *= spotlightMultiplier;
        }

        return strength;
    }
}