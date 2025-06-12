using System.Numerics;
using KeepersCompound.LGS;
using KeepersCompound.LGS.Database;
using KeepersCompound.LGS.Database.Chunks;
using KeepersCompound.LGS.Resources;
using Serilog;

namespace KeepersCompound.Lighting;

public enum CastSurfaceType
{
    Solid,
    Sky,
    Object,
    Air
}

public class Mesh(
    int triangleCount,
    List<Vector3> vertices,
    List<int> indices,
    List<CastSurfaceType> triangleSurfaceMap)
{
    public int TriangleCount { get; } = triangleCount;
    public Vector3[] Vertices { get; } = [..vertices];
    public int[] Indices { get; } = [..indices];
    public CastSurfaceType[] TriangleSurfaceMap { get; } = [..triangleSurfaceMap];
}

public class MeshBuilder
{
    private int _triangleCount = 0;
    private readonly List<Vector3> _vertices = [];
    private readonly List<int> _indices = [];
    private readonly List<CastSurfaceType> _primSurfaceMap = [];

    public void AddWorldRepPolys(WorldRep worldRep)
    {
        var polyVertices = new List<Vector3>();
        foreach (var cell in worldRep.Cells)
        {
            // We only care about polys representing solid terrain. We can't use RenderPolyCount because that includes
            // water surfaces.
            var solidPolys = cell.PolyCount - cell.PortalPolyCount;
            var cellIdxOffset = 0;
            for (var polyIdx = 0; polyIdx < solidPolys; polyIdx++)
            {
                var poly = cell.Polys[polyIdx];
                polyVertices.Clear();
                polyVertices.EnsureCapacity(poly.VertexCount);
                for (var i = 0; i < poly.VertexCount; i++)
                {
                    polyVertices.Add(cell.Vertices[cell.Indices[cellIdxOffset + i]]);
                }

                var primType = cell.RenderPolys[polyIdx].TextureId == 249 ? CastSurfaceType.Sky : CastSurfaceType.Solid;
                AddPolygon(polyVertices, primType);
                cellIdxOffset += poly.VertexCount;
            }
        }
    }

    public void AddObjectPolys(
        BrList brushList,
        ObjectHierarchy hierarchy,
        ResourceManager resources)
    {
        var polyVertices = new List<Vector3>();
        foreach (var brush in brushList.Brushes)
        {
            if (brush.Media != Media.Object)
            {
                continue;
            }

            var id = (int)brush.BrushInfo;
            var modelNameProp = hierarchy.GetProperty<PropLabel>(id, "P$ModelName");
            var scaleProp = hierarchy.GetProperty<PropVector>(id, "P$Scale");
            var renderTypeProp = hierarchy.GetProperty<PropRenderType>(id, "P$RenderTyp");
            var jointPosProp = hierarchy.GetProperty<PropJointPos>(id, "P$JointPos");
            var immobileProp = hierarchy.GetProperty<PropBool>(id, "P$Immobile");
            var staticShadowProp = hierarchy.GetProperty<PropBool>(id, "P$StatShad");

            var joints = jointPosProp?.Positions ?? [0, 0, 0, 0, 0, 0];
            var castsShadows = (immobileProp?.Value ?? false) || (staticShadowProp?.Value ?? false);
            var renderMode = renderTypeProp?.RenderMode ?? RenderMode.Normal;

            // TODO: Check which rendermodes cast shadows :)
            if (modelNameProp == null || !castsShadows || renderMode == RenderMode.CoronaOnly)
            {
                continue;
            }

            // Let's try and place an object :)
            // TODO: Handle failing to find model more gracefully
            // var modelName = modelNameProp.Value.ToLower() + ".bin";
            var modelName = $"obj/{modelNameProp.Value}.bin";
            if (!resources.TryGetModel(modelName, out var model))
            {
                Log.Warning("Failed to find model file: {Name}", modelName);
                continue;
            }

            model.ApplyJoints(joints);

            // Calculate base model transform
            var transform = Matrix4x4.CreateScale(scaleProp?.Value ?? Vector3.One);
            transform *= Matrix4x4.CreateRotationX(float.DegreesToRadians(brush.Angle.X));
            transform *= Matrix4x4.CreateRotationY(float.DegreesToRadians(brush.Angle.Y));
            transform *= Matrix4x4.CreateRotationZ(float.DegreesToRadians(brush.Angle.Z));
            transform *= Matrix4x4.CreateTranslation(brush.Position - model.Header.Center);

            // for each polygon slam its vertices and indices :)
            foreach (var poly in model.Polygons)
            {
                polyVertices.Clear();
                polyVertices.EnsureCapacity(poly.VertexCount);
                foreach (var idx in poly.VertexIndices)
                {
                    var vertex = model.Vertices[idx];
                    vertex = Vector3.Transform(vertex, transform);
                    polyVertices.Add(vertex);
                }

                AddPolygon(polyVertices, CastSurfaceType.Object);
            }
        }
    }

    private void AddPolygon(List<Vector3> vertices, CastSurfaceType castSurfaceType)
    {
        var vertexCount = vertices.Count;
        var indexOffset = _vertices.Count;

        // Polygons are n-sided, but fortunately they're convex so we can just do a fan triangulation
        // Embree triangle winding order is reverse of LGS winding order, so we go (0, i+1, i) instead of (0, i+1, i)
        _vertices.AddRange(vertices);
        for (var i = 1; i < vertexCount - 1; i++)
        {
            _indices.Add(indexOffset);
            _indices.Add(indexOffset + i + 1);
            _indices.Add(indexOffset + i);
            _primSurfaceMap.Add(castSurfaceType);
            _triangleCount++;
        }
    }

    public Mesh Build()
    {
        return new Mesh(_triangleCount, _vertices, _indices, _primSurfaceMap);
    }
}