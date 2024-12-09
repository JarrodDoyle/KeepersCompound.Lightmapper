using System.Numerics;

namespace KeepersCompound.Lightmapper;

public enum SurfaceType
{
    Solid,
    Sky,
    Water,
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

    public void AddPolygon(List<Vector3> vertices, SurfaceType surfaceType)
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