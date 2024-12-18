using System.Numerics;
using KeepersCompound.LGS;
using KeepersCompound.LGS.Database;
using KeepersCompound.LGS.Database.Chunks;

namespace KeepersCompound.Lightmapper;

// TODO: Rename to CastSurfaceType?
public enum SurfaceType
{
    Solid,
    Sky,
    Water,
    Air,
}

public class Mesh(int triangleCount, List<Vector3> vertices, List<int> indices, List<SurfaceType> triangleSurfaceMap)
{
    public int TriangleCount { get; } = triangleCount;
    public Vector3[] Vertices { get; } = [..vertices];
    public int[] Indices { get; } = [..indices];
    public SurfaceType[] TriangleSurfaceMap { get; } = [..triangleSurfaceMap];
}

public class MeshBuilder
{
    private int _triangleCount = 0;
    private readonly List<Vector3> _vertices = [];
    private readonly List<int> _indices = [];
    private readonly List<SurfaceType> _primSurfaceMap = [];

    public void AddWorldRepPolys(WorldRep worldRep)
    {
        var polyVertices = new List<Vector3>();
        foreach (var cell in worldRep.Cells)
        {
            var numPolys = cell.PolyCount;
            var numRenderPolys = cell.RenderPolyCount;
            var numPortalPolys = cell.PortalPolyCount;
            var solidPolys = numPolys - numPortalPolys;
            
            var cellIdxOffset = 0;
            for (var polyIdx = 0; polyIdx < numPolys; polyIdx++)
            {
                // There's 3 types of poly that we need to include in the mesh:
                // - Terrain
                // - Water surfaces
                // - Door vision blockers
                //
                // Door vision blockers are the interesting one. They're not RenderPolys at all, and we only include
                // them in the mesh if the cell only has two of them (otherwise the door is in the middle of the air)
                SurfaceType primType;
                if (polyIdx < solidPolys)
                {
                    primType = cell.RenderPolys[polyIdx].TextureId == 249 ? SurfaceType.Sky : SurfaceType.Solid;
                }
                else if (polyIdx < numRenderPolys)
                {
                    primType = SurfaceType.Water;
                }
                else if (cell is { Flags: 24, PortalPolyCount: 2 }) // TODO: Work out what these flags are!!
                {
                    primType = SurfaceType.Solid;
                }
                else
                {
                    continue;
                }
                
                var poly = cell.Polys[polyIdx];
                polyVertices.Clear();
                polyVertices.EnsureCapacity(poly.VertexCount);
                for (var i = 0; i < poly.VertexCount; i++)
                {
                    polyVertices.Add(cell.Vertices[cell.Indices[cellIdxOffset + i]]);
                }
                
                AddPolygon(polyVertices, primType);
                cellIdxOffset += poly.VertexCount;
            }
        }
    }

    public void AddObjectPolys(
        BrList brushList,
        ObjectHierarchy hierarchy,
        ResourcePathManager.CampaignResources campaignResources)
    {
        var polyVertices = new List<Vector3>();
        foreach (var brush in brushList.Brushes)
        {
            if (brush.media != BrList.Brush.Media.Object)
            {
                continue;
            }

            var id = (int)brush.brushInfo;
            var modelNameProp = hierarchy.GetProperty<PropLabel>(id, "P$ModelName");
            var scaleProp = hierarchy.GetProperty<PropVector>(id, "P$Scale");
            var renderTypeProp = hierarchy.GetProperty<PropRenderType>(id, "P$RenderTyp");
            var jointPosProp = hierarchy.GetProperty<PropJointPos>(id, "P$JointPos");
            var immobileProp = hierarchy.GetProperty<PropBool>(id, "P$Immobile");
            var staticShadowProp = hierarchy.GetProperty<PropBool>(id, "P$StatShad");

            var joints = jointPosProp?.Positions ?? [0, 0, 0, 0, 0, 0];
            var castsShadows = (immobileProp?.value ?? false) || (staticShadowProp?.value ?? false);
            var renderMode = renderTypeProp?.mode ?? PropRenderType.Mode.Normal;
            
            // TODO: Check which rendermodes cast shadows :)
            if (modelNameProp == null || !castsShadows || renderMode == PropRenderType.Mode.CoronaOnly)
            {
                continue;
            }
            
            // Let's try and place an object :)
            var modelName = modelNameProp.value.ToLower() + ".bin";
            var modelPath = campaignResources.GetResourcePath(ResourceType.Object, modelName);
            if (modelPath == null)
            {
                continue;
            }
            
            // TODO: Handle failing to find model more gracefully
            var pos = brush.position;
            var rot = brush.angle;
            var scale = scaleProp?.value ?? Vector3.One;
            var model = new ModelFile(modelPath);
            pos -= model.Header.Center;


            var scalePart = Matrix4x4.CreateScale(scale);
            var rotPart = Matrix4x4.Identity;
            rotPart *= Matrix4x4.CreateRotationX(float.DegreesToRadians(rot.X));
            rotPart *= Matrix4x4.CreateRotationY(float.DegreesToRadians(rot.Y));
            rotPart *= Matrix4x4.CreateRotationZ(float.DegreesToRadians(rot.Z));
            var transPart = Matrix4x4.CreateTranslation(pos);
            var modelTrans = scalePart * rotPart * transPart;
            
            // for each object modify the vertices
            // TODO: Handle nested sub objects
            foreach (var subObj in model.Objects)
            {
                var jointTrans = Matrix4x4.Identity;
                if (subObj.Joint != -1)
                {
                    var ang = float.DegreesToRadians(joints[subObj.Joint]);
                    var jointRot = Matrix4x4.CreateFromYawPitchRoll(0, ang, 0);
                    var objTrans = subObj.Transform;
                    jointTrans = jointRot * objTrans;
                }
                
                var transform = jointTrans * modelTrans;
                
                var start = subObj.PointIdx;
                var end = start + subObj.PointCount;
                for (var i = start; i < end; i++)
                {
                    var v = model.Vertices[i];
                    model.Vertices[i] = Vector3.Transform(v, transform);
                }
            }
            
            // for each polygon slam its vertices and indices :)
            foreach (var poly in model.Polygons)
            {
                polyVertices.Clear();
                polyVertices.EnsureCapacity(poly.VertexCount);
                foreach (var idx in poly.VertexIndices)
                {
                    polyVertices.Add(model.Vertices[idx]);
                }
                
                AddPolygon(polyVertices, SurfaceType.Solid);
            }
        }
    }

    private void AddPolygon(List<Vector3> vertices, SurfaceType surfaceType)
    {
        var vertexCount = vertices.Count;
        var indexOffset = _vertices.Count;

        // Polygons are n-sided, but fortunately they're convex so we can just do a fan triangulation
        _vertices.AddRange(vertices);
        for (var i = 1; i < vertexCount - 1; i++)
        {
            _indices.Add(indexOffset);
            _indices.Add(indexOffset + i);
            _indices.Add(indexOffset + i + 1);
            _primSurfaceMap.Add(surfaceType);
            _triangleCount++;
        }
    }

    public Mesh Build()
    {
        return new Mesh(_triangleCount, _vertices, _indices, _primSurfaceMap);
    }
}