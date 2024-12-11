using System.Numerics;
using KeepersCompound.LGS;
using KeepersCompound.LGS.Database;
using KeepersCompound.LGS.Database.Chunks;
using TinyEmbree;

namespace KeepersCompound.Lightmapper;

public class LightMapper
{
    // The objcast element of sunlight is ignored, we just care if it's quadlit
    private struct SunSettings
    {
        public bool Enabled;
        public bool QuadLit;
        public Vector3 Direction;
        public Vector3 Color;
    }
    
    private struct Settings
    {
        public Vector3 AmbientLight;
        public bool Hdr;
        public SoftnessMode MultiSampling;
        public float MultiSamplingCenterWeight;
        public bool LightmappedWater;
        public SunSettings Sunlight;
    }

    private ResourcePathManager.CampaignResources _campaign;
    private string _misPath;
    private DbFile _mission;
    private ObjectHierarchy _hierarchy;
    private Raytracer _scene;
    private List<Light> _lights;
    private SurfaceType[] _triangleTypeMap;

    public LightMapper(
        string installPath,
        string campaignName,
        string missionName)
    {
        var pathManager = SetupPathManager(installPath);
        _campaign = pathManager.GetCampaign(campaignName);
        _misPath = _campaign.GetResourcePath(ResourceType.Mission, missionName);
        _mission = Timing.TimeStage("Parse DB", () => new DbFile(_misPath));
        _hierarchy = Timing.TimeStage("Build Hierarchy", BuildHierarchy);
        _lights = [];
        
        var mesh = Timing.TimeStage("Build Mesh", BuildMesh);
        _triangleTypeMap = mesh.TriangleSurfaceMap;
        _scene = Timing.TimeStage("Build RT Scene", () =>
        {
            var rt = new Raytracer();
            rt.AddMesh(new TriangleMesh(mesh.Vertices, mesh.Indices));
            rt.CommitScene();
            return rt;
        });
    }
    
    public void Light()
    {
        // TODO: Throw?
        if (!_mission.TryGetChunk<RendParams>("RENDPARAMS", out var rendParams) ||
            !_mission.TryGetChunk<LmParams>("LM_PARAM", out var lmParams) ||
            !_mission.TryGetChunk<WorldRep>("WREXT", out var worldRep))
        {
            return;
        }

        var sunlightSettings = new SunSettings()
        {
            Enabled = rendParams.useSunlight,
            QuadLit = rendParams.sunlightMode is RendParams.SunlightMode.QuadUnshadowed or RendParams.SunlightMode.QuadObjcastShadows,
            Direction = Vector3.Normalize(rendParams.sunlightDirection),
            Color = Utils.HsbToRgb(rendParams.sunlightHue, rendParams.sunlightSaturation, rendParams.sunlightBrightness),
        };

        // TODO: lmParams LightmappedWater doesn't mean the game will actually *use* the lightmapped water hmm
        var settings = new Settings
        {
            Hdr = worldRep.DataHeader.LightmapFormat == 2,
            AmbientLight = rendParams.ambientLight * 255,
            MultiSampling = lmParams.ShadowSoftness,
            MultiSamplingCenterWeight = lmParams.CenterWeight,
            LightmappedWater = lmParams.LightmappedWater,
            Sunlight = sunlightSettings,
        };
        
        Timing.TimeStage("Gather Lights", BuildLightList);
        Timing.TimeStage("Set Light Indices", () => SetCellLightIndices(settings));
        Timing.TimeStage("Trace Scene", () => TraceScene(settings));
        Timing.TimeStage("Update AnimLight Cell Mapping", SetAnimLightCellMaps);

        // We always do object casting, so it's nice to let dromed know that :)
        lmParams.ShadowType = LmParams.LightingMode.Objcast;
        if (rendParams is { useSunlight: true, sunlightMode: RendParams.SunlightMode.SingleUnshadowed })
        {
            rendParams.sunlightMode = RendParams.SunlightMode.SingleObjcastShadows;
        } else if (rendParams is { useSunlight: true, sunlightMode: RendParams.SunlightMode.QuadUnshadowed })
        {
            rendParams.sunlightMode = RendParams.SunlightMode.QuadObjcastShadows;
        }
    }

    public void Save(string missionName)
    {
        var ext = Path.GetExtension(_misPath);
        var dir = Path.GetDirectoryName(_misPath);
        var savePath = Path.Join(dir, missionName + ext);
        Timing.TimeStage("Save DB", () => _mission.Save(savePath));
    }

    private static ResourcePathManager SetupPathManager(string installPath)
    {
        var tmpDir = Directory.CreateTempSubdirectory("KCLightmapper");
        var resPathManager = new ResourcePathManager(tmpDir.FullName);
        resPathManager.Init(installPath);
        return resPathManager;
    }

    private ObjectHierarchy BuildHierarchy()
    {
        if (!_mission.TryGetChunk<GamFile>("GAM_FILE", out var gamFile))
        {
            return new ObjectHierarchy(_mission);
        }
        
        var dir = Path.GetDirectoryName(_misPath);
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var name = gamFile.fileName;
        var paths = Directory.GetFiles(dir!, name, options);
        if (paths.Length > 0)
        {
            return new ObjectHierarchy(_mission, new DbFile(paths[0]));
        }
        return new ObjectHierarchy(_mission);
    }

    private Mesh BuildMesh()
    {
        var meshBuilder = new MeshBuilder();

        // TODO: Should this throw?
        // TODO: Only do object polys if objcast lighting?
        if (!_mission.TryGetChunk<WorldRep>("WREXT", out var worldRep) ||
            !_mission.TryGetChunk<BrList>("BRLIST", out var brList))
        {
            return meshBuilder.Build();
        }

        meshBuilder.AddWorldRepPolys(worldRep);
        meshBuilder.AddObjectPolys(brList, _hierarchy, _campaign);
        return meshBuilder.Build();
    }

    private void BuildLightList()
    {
        _lights.Clear();

        // Get the chunks we need
        if (!_mission.TryGetChunk<WorldRep>("WREXT", out var worldRep) ||
            !_mission.TryGetChunk<BrList>("BRLIST", out var brList))
        {
            return;
        }

        worldRep.LightingTable.Reset();
        
        // TODO: Calculate the actual effective radius of infinite lights
        // potentially do the same for all lights and lower their radius if necessary?
        foreach (var brush in brList.Brushes)
        {
            switch (brush.media)
            {
                case BrList.Brush.Media.Light:
                    ProcessBrushLight(worldRep.LightingTable, brush);
                    break;
                case BrList.Brush.Media.Object:
                    ProcessObjectLight(worldRep.LightingTable, brush);
                    break;
            }
        }

        var infinite = _lights.Count(light => light.Radius == float.MaxValue);
        if (infinite > 0)
        {
            Console.WriteLine($"WARNING: Infinite radius lights found: {infinite}/{_lights.Count}");
        }
    }
    
    // TODO: Check if this works (brush is a record type)
    private void ProcessBrushLight(WorldRep.LightTable lightTable, BrList.Brush brush)
    {
        // For some reason the light table index on brush lights is 1 indexed
        brush.brushInfo = (uint)lightTable.LightCount + 1;
        var sz = brush.size;
        
        // Ignore 0 brightness lights
        if (sz.X == 0)
        {
            return;
        }
        
        var light = new Light
        {
            Position = brush.position,
            Color = Utils.HsbToRgb(sz.Y, sz.Z, Math.Min(sz.X, 255.0f)),
            Radius = float.MaxValue,
            R2 = float.MaxValue,
            LightTableIndex = lightTable.LightCount,
        };
        
        _lights.Add(light);
        lightTable.AddLight(light.ToLightData(32.0f));
    }

    private void ProcessObjectLight(WorldRep.LightTable lightTable, BrList.Brush brush)
    {
        // TODO: Handle PropSpotlightAndAmbient
        var id = (int)brush.brushInfo;
        var propAnimLight = _hierarchy.GetProperty<PropAnimLight>(id, "P$AnimLight", false);
        var propLight = _hierarchy.GetProperty<PropLight>(id, "P$Light", false);
        var propLightColor = _hierarchy.GetProperty<PropLightColor>(id, "P$LightColo");
        var propSpotlight = _hierarchy.GetProperty<PropSpotlight>(id, "P$Spotlight");
        var propModelName = _hierarchy.GetProperty<PropLabel>(id, "P$ModelName");

        propLightColor ??= new PropLightColor { Hue = 0, Saturation = 0 };
        
        // TODO: Also apply scale?
        var rot = Matrix4x4.Identity;
        rot *= Matrix4x4.CreateRotationX(float.DegreesToRadians(brush.angle.X));
        rot *= Matrix4x4.CreateRotationY(float.DegreesToRadians(brush.angle.Y));
        rot *= Matrix4x4.CreateRotationZ(float.DegreesToRadians(brush.angle.Z));
        
        var baseLight = new Light
        {
            Position = brush.position,
            SpotlightDir = Vector3.Transform(-Vector3.UnitZ, rot),
            SpotlightInnerAngle = -1.0f,
        };

        if (propModelName != null)
        {
            var resName = $"{propModelName.value.ToLower()}.bin";
            var modelPath = _campaign.GetResourcePath(ResourceType.Object, resName);
            if (modelPath != null)
            {
                // TODO: Handle failing to find model more gracefully
                var model = new ModelFile(modelPath);
                if (model.TryGetVhot(ModelFile.VhotId.LightPosition, out var vhot))
                {
                    baseLight.Position += Vector3.Transform(vhot.Position - model.Header.Center, rot);
                    
                }
                if (model.TryGetVhot(ModelFile.VhotId.LightDirection, out vhot))
                {
                    baseLight.SpotlightDir = Vector3.Transform(vhot.Position - model.Header.Center, rot);
                }
            }

        }

        if (propSpotlight != null)
        {
            baseLight.Spotlight = true;
            baseLight.SpotlightInnerAngle = (float)Math.Cos(float.DegreesToRadians(propSpotlight.InnerAngle));
            baseLight.SpotlightOuterAngle = (float)Math.Cos(float.DegreesToRadians(propSpotlight.OuterAngle));
        }

        if (propLight != null && propLight.Brightness != 0f)
        {
            var light = new Light
            {
                Position = baseLight.Position + Vector3.Transform(propLight.Offset, rot),
                Color = Utils.HsbToRgb(propLightColor.Hue, propLightColor.Saturation, propLight.Brightness),
                InnerRadius = propLight.InnerRadius,
                Radius = propLight.Radius,
                R2 = propLight.Radius * propLight.Radius,
                QuadLit = propLight.QuadLit,
                Spotlight = baseLight.Spotlight,
                SpotlightDir = baseLight.SpotlightDir,
                SpotlightInnerAngle = baseLight.SpotlightInnerAngle,
                SpotlightOuterAngle = baseLight.SpotlightOuterAngle,
                LightTableIndex = lightTable.LightCount,
            };
            if (propLight.Radius == 0)
            {
                light.Radius = float.MaxValue;
                light.R2 = float.MaxValue;
            }

            _lights.Add(light);
            lightTable.AddLight(light.ToLightData(32.0f));
        }

        if (propAnimLight != null)
        {
            var lightIndex = lightTable.LightCount;
            propAnimLight.LightTableLightIndex = (ushort)lightIndex;

            var light = new Light
            {
                Position = baseLight.Position + Vector3.Transform(propAnimLight.Offset, rot),
                Color = Utils.HsbToRgb(propLightColor.Hue, propLightColor.Saturation, propAnimLight.MaxBrightness),
                InnerRadius = propAnimLight.InnerRadius,
                Radius = propAnimLight.Radius,
                R2 = propAnimLight.Radius * propAnimLight.Radius,
                QuadLit = propAnimLight.QuadLit,
                Spotlight = baseLight.Spotlight,
                SpotlightDir = baseLight.SpotlightDir,
                SpotlightInnerAngle = baseLight.SpotlightInnerAngle,
                SpotlightOuterAngle = baseLight.SpotlightOuterAngle,
                Anim = true,
                ObjId = id,
                LightTableIndex = propAnimLight.LightTableLightIndex,
            };
            if (propAnimLight.Radius == 0)
            {
                light.Radius = float.MaxValue;
                light.R2 = float.MaxValue;
            }

            _lights.Add(light);
            lightTable.AddLight(light.ToLightData(32.0f));
        }
    }

    private void SetCellLightIndices(Settings settings)
    {
        // TODO: Doors aren't blocking lights. Need to do some cell traversal to remove light indices :(
        
        if (!_mission.TryGetChunk<WorldRep>("WREXT", out var worldRep))
            return;
        
        // TODO: Move this functionality to the LGS library
        // We set up light indices in separately from lighting because the actual
        // lighting phase takes a lot of shortcuts that we don't want
        Parallel.ForEach(worldRep.Cells, cell =>
        {
            cell.LightIndexCount = 0;
            cell.LightIndices.Clear();
            
            // The first element of the light indices array is used to store how many
            // actual lights are in the list. Which is just LightIndexCount - 1...
            // Odd choice I know
            cell.LightIndexCount++;
            cell.LightIndices.Add(0);
            
            // If we have sunlight, then we just assume the sun has the potential to reach everything (ew)
            // The sun enabled option doesn't actually seem to do anything at runtime, it's purely about if
            // the cell has the sunlight idx on it.
            if (settings.Sunlight.Enabled)
            {
                cell.LightIndexCount++;
                cell.LightIndices.Add(0);
                cell.LightIndices[0]++;
            }

            // The OG lightmapper uses the cell traversal to work out all the cells that
            // are actually visited. We're a lot more coarse and just say if a cell is
            // in range then we potentially affect the lighting in the cell and add it 
            // to the list.
            // There's a soft length limit here of 96 due to the runtime object shadow
            // cache, so we want this to be as minimal as possible. Additionally large
            // lists actually cause performance issues!
            var cellAabb = new MathUtils.Aabb(cell.Vertices);
            foreach (var light in _lights)
            {
                if (MathUtils.Intersects(new MathUtils.Sphere(light.Position, light.Radius), cellAabb))
                {
                    cell.LightIndexCount++;
                    cell.LightIndices.Add((ushort)light.LightTableIndex);
                    cell.LightIndices[0]++;
                }
            }

            if (cell.LightIndexCount > 97)
            {
                Console.WriteLine($"WARNING: Too many lights in cell at ({cell.SphereCenter}): {cell.LightIndexCount - 1} / 96");
            }
        });

        {
            var overLit = 0;
            var maxLights = 0;
            foreach (var cell in worldRep.Cells)
            {
                if (cell.LightIndexCount > 97)
                {
                    overLit++;
                }

                if (cell.LightIndexCount > maxLights)
                {
                    maxLights = cell.LightIndexCount - 1;
                }
            }

            if (overLit > 0)
            {
                Console.WriteLine($"WARNING: Overlit cells detected: {overLit}/{worldRep.Cells.Length}, MaxLights: {maxLights} / 96");
            }
        }
    }

    private void TraceScene(Settings settings)
    {
        if (!_mission.TryGetChunk<WorldRep>("WREXT", out var worldRep))
        {
            return;
        }
        
        Parallel.ForEach(worldRep.Cells, cell =>
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

            var solidPolys = numPolys - numPortalPolys;
            var cellIdxOffset = 0;
            for (var polyIdx = 0; polyIdx < numRenderPolys; polyIdx++)
            {
                var poly = cell.Polys[polyIdx];
                var plane = cell.Planes[poly.PlaneId];
                var renderPoly = cell.RenderPolys[polyIdx];
                var info = cell.LightList[polyIdx];
                var lightmap = cell.Lightmaps[polyIdx];

                info.AnimLightBitmask = 0;
                
                // We have to reset the lightmaps for water, but we don't want to do anything else
                var waterPoly = polyIdx >= solidPolys;
                if (!settings.LightmappedWater && waterPoly)
                {
                    lightmap.Reset(Vector3.One * 255f, settings.Hdr);
                    continue;
                }
                lightmap.Reset(settings.AmbientLight, settings.Hdr);

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
                
                var (texU, texV) = renderPoly.TextureVectors;
                var (offsets, weights) =
                    GetTraceOffsetsAndWeights(settings.MultiSampling, texU, texV, settings.MultiSamplingCenterWeight);
                var (quadOffsets, quadWeights) = settings.MultiSampling == SoftnessMode.HighFourPoint
                    ? (offsets, weights)
                    : GetTraceOffsetsAndWeights(SoftnessMode.HighFourPoint, texU, texV, settings.MultiSamplingCenterWeight);

                for (var y = 0; y < lightmap.Height; y++)
                {
                    for (var x = 0; x < lightmap.Width; x++)
                    {
                        var pos = topLeft;
                        pos += x * 0.25f * renderPoly.TextureVectors.Item1;
                        pos += y * 0.25f * renderPoly.TextureVectors.Item2;
                        
                        // TODO: Handle quad lit lights better. Right now we're computing two sets of points for every
                        // luxel. Maybe it's better to only compute if we encounter a quadlit light?
                        var tracePoints = GetTracePoints(pos, offsets, renderPoly.Center, planeMapper, v2ds);
                        var quadTracePoints = settings.MultiSampling == SoftnessMode.HighFourPoint
                            ? tracePoints
                            : GetTracePoints(pos, quadOffsets, renderPoly.Center, planeMapper, v2ds);
                        
                        // This is almost perfect now. Any issues seem to be related to Dark not carrying HSB strength correctly
                        if (settings.Sunlight.Enabled) {
                            // Check if plane normal is facing towards the light
                            // If it's not then we're never going to be (directly) lit by this
                            // light.
                            var sunAngle = Vector3.Dot(-settings.Sunlight.Direction, plane.Normal);
                            if (sunAngle > 0)
                            {
                                var strength = 0f;
                                var targetPoints = settings.Sunlight.QuadLit ? quadTracePoints : tracePoints;
                                var targetWeights = settings.Sunlight.QuadLit ? quadWeights : weights;
                                for (var idx = 0; idx < targetPoints.Length; idx++)
                                {
                                    var point = targetPoints[idx];
                                    if (TraceSunRay(point, -settings.Sunlight.Direction))
                                    {
                                        // Sunlight is a simpler lighting algorithm than normal lights so we can just
                                        // do it here
                                        strength += targetWeights[idx] * sunAngle;
                                    }
                                }

                                if (strength != 0f)
                                {
                                    lightmap.AddLight(0, x, y, settings.Sunlight.Color, strength, settings.Hdr);
                                }
                            }
                        }

                        foreach (var light in _lights)
                        {
                            var layer = 0;

                            // Check if plane normal is facing towards the light
                            // If it's not then we're never going to be (directly) lit by this
                            // light.
                            var centerDirection = renderPoly.Center - light.Position;
                            if (Vector3.Dot(plane.Normal, centerDirection) >= 0)
                            {
                                continue;
                            }

                            // If there aren't *any* points on the plane that are in range of the light
                            // then none of the lightmap points will be so we can discard.
                            // The more compact a map is the less effective this is
                            var planeDist = MathUtils.DistanceFromPlane(plane, light.Position);
                            if (planeDist > light.Radius)
                            {
                                continue;
                            }

                            // If the poly of the lightmap doesn't intersect the light radius then
                            // none of the lightmap points will so we can discard.
                            if (!MathUtils.Intersects(new MathUtils.Sphere(light.Position, light.Radius), aabb))
                            {
                                continue;
                            }
                            
                            var strength = 0f;
                            var targetPoints = light.QuadLit ? quadTracePoints : tracePoints;
                            var targetWeights = light.QuadLit ? quadWeights : weights;
                            for (var idx = 0; idx < targetPoints.Length; idx++)
                            {
                                var point = targetPoints[idx];
                                
                                // If we're out of range there's no point casting a ray
                                // There's probably a better way to discard the entire lightmap
                                // if we're massively out of range
                                if ((point - light.Position).LengthSquared() > light.R2)
                                {
                                    continue;
                                }
                                
                                if (TraceRay(light.Position, point))
                                {
                                    strength += targetWeights[idx] * light.StrengthAtPoint(point, plane);
                                }
                            }

                            if (strength != 0f)
                            {
                                // If we're an anim light there's a lot of stuff we need to update
                                // Firstly we need to add the light to the cells anim light palette
                                // Secondly we need to set the appropriate bit of the lightmap's
                                // bitmask. Finally we need to check if the lightmap needs another layer
                                if (light.Anim)
                                {
                                    // TODO: Don't recalculate this for every point lol
                                    var paletteIdx = cell.AnimLights.IndexOf((ushort)light.LightTableIndex);
                                    if (paletteIdx == -1)
                                    {
                                        paletteIdx = cell.AnimLightCount;
                                        cell.AnimLightCount++;
                                        cell.AnimLights.Add((ushort)light.LightTableIndex);
                                    }
                                    info.AnimLightBitmask |= 1u << paletteIdx;
                                    layer = paletteIdx + 1;
                                }
                                lightmap.AddLight(layer, x, y, light.Color, strength, settings.Hdr);
                            }
                        }
                    }
                }

                cellIdxOffset += poly.VertexCount;
            }
        });
    }
    
    private static (Vector3[], float[]) GetTraceOffsetsAndWeights(
        SoftnessMode mode,
        Vector3 texU,
        Vector3 texV,
        float centerWeight)
    {
        var offsetScale = mode switch
        {
            SoftnessMode.HighFourPoint or SoftnessMode.HighFivePoint or SoftnessMode.HighNinePoint => 4f,
            SoftnessMode.MediumFourPoint or SoftnessMode.MediumFivePoint or SoftnessMode.MediumNinePoint => 8f,
            SoftnessMode.LowFourPoint => 16f,
            _ => 1f,
        };

        var cw = centerWeight;
        var w = 1f - cw;
        texU /= offsetScale;
        texV /= offsetScale;

        return mode switch
        {
            SoftnessMode.LowFourPoint or SoftnessMode.MediumFourPoint or SoftnessMode.HighFourPoint => (
                [-texU - texV, -texU - texV, -texU + texV, texU + texV],
                [0.25f, 0.25f, 0.25f, 0.25f]),
            SoftnessMode.MediumFivePoint or SoftnessMode.HighFivePoint => (
                [Vector3.Zero, -texU - texV, texU - texV, -texU + texV, texU + texV],
                [cw, w * 0.25f, w * 0.25f, w * 0.25f, w * 0.25f]),
            SoftnessMode.MediumNinePoint or SoftnessMode.HighNinePoint => (
                [Vector3.Zero, -texU - texV, texU - texV, -texU + texV, texU + texV, -texU, texU, -texV, texV],
                [cw, w * 0.125f, w * 0.125f, w * 0.125f, w * 0.125f, w * 0.125f, w * 0.125f, w * 0.125f, w * 0.125f]),
            _ => (
                [Vector3.Zero],
                [1f]),
        };
    }

    private Vector3[] GetTracePoints(
        Vector3 basePosition,
        Vector3[] offsets,
        Vector3 polyCenter,
        MathUtils.PlanePointMapper planeMapper,
        Vector2[] v2ds)
    {
        var tracePoints = new Vector3[offsets.Length];
        for (var i = 0; i < offsets.Length; i++)
        {
            var offset = offsets[i];
            var pos = basePosition + offset;
            
            // Embree has robustness issues when hitting poly edges which
            // results in false misses. To alleviate this we pre-push everything
            // slightly towards the center of the poly.
            var centerOffset = polyCenter - pos;
            if (centerOffset.LengthSquared() > MathUtils.Epsilon)
            {
                pos += Vector3.Normalize(centerOffset) * MathUtils.Epsilon;
            }
            
            // If we can't see our target point from the center of the poly
            // then it's outside the world. We need to clip the point to slightly
            // inside the poly and retrace to avoid three problems:
            // 1. Darkened spots from lightmap pixels whose center is outside
            //    the polygon but is partially contained in the polygon
            // 2. Darkened spots from linear filtering of points outside the
            //    polygon which have missed
            // 3. Darkened spots where centers are on the exact edge of a poly
            //    which can sometimes cause Embree to miss casts
            var inPoly = TraceRay(polyCenter + planeMapper.Normal * 0.25f, pos);
            if (!inPoly)
            {
                var p2d = planeMapper.MapTo2d(pos);
                p2d = MathUtils.ClipPointToPoly2d(p2d, v2ds);
                pos = planeMapper.MapTo3d(p2d);
            }

            tracePoints[i] = pos;
        }
        
        return tracePoints;
    }
    
    private bool TraceRay(Vector3 origin, Vector3 target)
    {
        var hitDistanceFromTarget = float.MinValue;
        var hitSurfaceType = SurfaceType.Water;
        while (hitDistanceFromTarget < -MathUtils.Epsilon && hitSurfaceType == SurfaceType.Water)
        {
            var direction = target - origin;
            var hitResult = _scene.Trace(new Ray
            {
                Origin = origin,
                Direction = Vector3.Normalize(direction),
            });

            hitDistanceFromTarget = hitResult.Distance - direction.Length();
            hitSurfaceType = _triangleTypeMap[(int)hitResult.PrimId];
            origin = hitResult.Position += direction * MathUtils.Epsilon;
        }

        return Math.Abs(hitDistanceFromTarget) < MathUtils.Epsilon;
    }

    // TODO: Can this be merged with the above?
    private bool TraceSunRay(Vector3 origin, Vector3 direction)
    {
        // Avoid self intersection
        origin += direction * MathUtils.Epsilon;
        
        var hitSurfaceType = SurfaceType.Water;
        while (hitSurfaceType == SurfaceType.Water)
        {
            var hitResult = _scene.Trace(new Ray
            {
                Origin = origin,
                Direction = Vector3.Normalize(direction),
            });
            
            hitSurfaceType = _triangleTypeMap[(int)hitResult.PrimId];
            origin = hitResult.Position += direction * MathUtils.Epsilon;
        }
        
        return hitSurfaceType == SurfaceType.Sky;
    }

    private void SetAnimLightCellMaps()
    {
        if (!_mission.TryGetChunk<PropertyChunk<PropAnimLight>>("P$AnimLight", out var animLightChunk) ||
            !_mission.TryGetChunk<WorldRep>("WREXT", out var worldRep))
        {
            return;
        }
        
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

        
        foreach (var (lightIdx, animCellMaps) in map)
        {
            // We need to update the object property so it knows its mapping range
            // TODO: Handle nulls
            var light = _lights.Find(l => l.Anim && l.LightTableIndex == lightIdx);
            var prop = animLightChunk.properties.Find(p => p.objectId == light.ObjId);
            prop.LightTableLightIndex = lightIdx;
            prop.LightTableMapIndex = (ushort)worldRep.LightingTable.AnimMapCount;
            prop.CellsReached = (ushort)animCellMaps.Count;

            worldRep.LightingTable.AnimCellMaps.AddRange(animCellMaps);
            worldRep.LightingTable.AnimMapCount += animCellMaps.Count;
        }
    }
}