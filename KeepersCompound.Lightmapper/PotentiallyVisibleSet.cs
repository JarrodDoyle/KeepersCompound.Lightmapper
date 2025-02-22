using System.Collections;
using System.Numerics;
using KeepersCompound.LGS.Database.Chunks;
using Serilog;

namespace KeepersCompound.Lightmapper;

public class PotentiallyVisibleSet
{
    private readonly struct Node(List<int> edgeIndices)
    {
        public readonly List<int> EdgeIndices = edgeIndices;
    }
    
    private readonly struct Edge(int mightSeeLength, int destination, Poly poly)
    {
        public readonly BitArray MightSee = new(mightSeeLength);
        public readonly int Destination = destination;
        public readonly Poly Poly = poly;

        public override string ToString()
        {
            return $"<Destination: {Destination}, Poly: {Poly}";
        }
    }

    private struct Poly
    {
        public readonly Vector3 Center;
        public readonly float Radius;
        public List<Vector3> Vertices;
        public readonly Plane Plane;

        public Poly(List<Vector3> vertices, Plane plane)
        {
            Vertices = vertices;
            Plane = plane;

            // Center is just taken to be the "average" of the vertices
            Center = Vector3.Zero;
            foreach (var v in vertices)
            {
                Center += v;
            }
            
            Center /= vertices.Count;
            
            // Radius is the max vertex distance from the center
            // We're actually calculating radius squared to begin with because it's faster :)
            Radius = 0;
            foreach (var v in vertices)
            {
                Radius = float.Max(Radius, (v - Center).LengthSquared());
            }
            
            Radius = MathF.Sqrt(Radius);
        }

        public Poly(Poly other)
        {
            Vertices = [..other.Vertices];
            Plane = other.Plane;
        }
        
        public bool IsCoplanar(Poly other)
        {
            return MathUtils.IsCoplanar(Plane, other.Plane);
        }
        
        public override string ToString()
        {
            return $"<Plane: {Plane}, Vertices: [{string.Join(", ", Vertices)}]";
        }
    }

    private readonly Node[] _graph;
    private readonly List<Edge> _edges;

    private const float Epsilon = MathUtils.Epsilon;

    // TODO:
    // - This is a conservative algorithm based on Matt's Ramblings Quake PVS video
    // - Build portal graph (or just use WR directly)
    // - A cell can always see it's self and any immediate neighbours
    // - The third depth cell is also visible unless the portal to it is coplanar with the second cells portal (do I need to think about this?)
    // - For all further cells:
    //   - Generate separating planes between the source cell portal and the previously passed (clipped) portal
    //   - Clip the target portal to the new cell using the separating planes
    //   - If anything is left of the clipped portal, we can see, otherwise we discard that cell
    // - The full process is a recursive depth first search

    public PotentiallyVisibleSet(WorldRep.Cell[] cells)
    {
        _graph = new Node[cells.Length];

        var portalCount = 0;
        for (var i = 0; i < cells.Length; i++)
        {
            portalCount += cells[i].PortalPolyCount;
        }
        
        _edges = new List<Edge>(portalCount);
        Log.Information("Mission contains {PortalCount} portals.", portalCount);
        
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            var edgeIndices = new List<int>(cell.PortalPolyCount);

            // If a cell is "blocks vision" flagged, we can never see out of it
            // We can see into it though, so we still want the edges coming in
            if ((cell.Flags & 8) != 0)
            {
                _graph[i] = new Node(edgeIndices);
                continue;
            }
            
            // We have to cycle through *all* polys rather than just portals to calculate the correct poly vertex offsets
            var indicesOffset = 0;
            var portalStartIdx = cell.PolyCount - cell.PortalPolyCount;
            for (var j = 0; j < cell.PolyCount; j++)
            {
                var poly = cell.Polys[j];
                if (j < portalStartIdx)
                {
                    indicesOffset += poly.VertexCount;
                    continue;
                }
                
                // Checking if there's already an edge is super slow. It's much faster to just add a new edge, even with
                // the duplicated poly
                var vs = new List<Vector3>(poly.VertexCount);
                for (var vIdx = 0; vIdx < poly.VertexCount; vIdx++)
                {
                    vs.Add(cell.Vertices[cell.Indices[indicesOffset + vIdx]]);
                }

                var edge = new Edge(cells.Length, poly.Destination, new Poly(vs, cell.Planes[poly.PlaneId]));
                edgeIndices.Add(_edges.Count);
                _edges.Add(edge);
                indicesOffset += poly.VertexCount;
            }

            _graph[i] = new Node(edgeIndices);
        }

        // Parallel.ForEach(_edges, ComputeEdgeMightSee);
    }

    public HashSet<int> ComputeVisibilityFast(int cellIdx)
    {
        if (cellIdx >= _graph.Length)
        {
            return [];
        }

        var visibleCells = new HashSet<int>();
        foreach (var edgeIdx in _graph[cellIdx].EdgeIndices)
        {
            var edge = _edges[edgeIdx];
            for (var i = 0; i < edge.MightSee.Length; i++)
            {
                if (edge.MightSee[i])
                {
                    visibleCells.Add(i);
                }
            }
        }
        
        return visibleCells;
    }
    
    // TODO: Max distance :)
    public HashSet<int> ComputeVisibilityExact(Vector3 pos, int cellIdx)
    {
        if (cellIdx >= _graph.Length)
        {
            return [];
        }

        var visibleCells = new HashSet<int> { cellIdx };
        var visited = new Stack<int>();
        visited.Push(cellIdx);

        foreach (var edgeIdx in _graph[cellIdx].EdgeIndices)
        {
            var edge = _edges[edgeIdx];
            ComputeVisibilityExactRecursive(pos, visibleCells, visited, edge.Destination, edge.Poly);
        }
        
        return visibleCells;
    }

    private void ComputeVisibilityExactRecursive(
        Vector3 lightPos,
        HashSet<int> visibleCells,
        Stack<int> visited,
        int currentCellIdx,
        Poly passPoly)
    {
        visited.Push(currentCellIdx);
        visibleCells.Add(currentCellIdx);

        var clipPlanes = new List<Plane>(passPoly.Vertices.Count);
        clipPlanes.Clear();
        for (var i = 0; i < passPoly.Vertices.Count; i++)
        {
            var v0 = passPoly.Vertices[i];
            var v1 = passPoly.Vertices[(i + 1) % passPoly.Vertices.Count];

            var normal = Vector3.Cross(v0 - lightPos, v1 - lightPos);
            if (normal.LengthSquared() < Epsilon)
            {
                continue;
            }

            normal = Vector3.Normalize(normal);
            var d = -Vector3.Dot(v1, normal);
            var plane = new Plane(normal, d);
            clipPlanes.Add(plane);
        }
        
        foreach (var targetEdgeIdx in _graph[currentCellIdx].EdgeIndices)
        {
            var targetEdge = _edges[targetEdgeIdx];
            if (visited.Contains(targetEdge.Destination) || passPoly.IsCoplanar(targetEdge.Poly))
            {
                continue;
            }

            var poly = new Poly(targetEdge.Poly);
            foreach (var clipPlane in clipPlanes)
            {
                ClipPolygonByPlane(ref poly, clipPlane);
            }
            
            if (poly.Vertices.Count == 0)
            {
                continue;
            }

            ComputeVisibilityExactRecursive(lightPos, visibleCells, visited, targetEdge.Destination, poly);
        }

        visited.Pop();
    }
    
    public void ComputeCellMightSee(int cellIdx)
    {
        if (cellIdx >= _graph.Length)
        {
            return;
        }
        
        foreach (var edgeIdx in _graph[cellIdx].EdgeIndices)
        {
            ComputeEdgeMightSee(_edges[edgeIdx]);
        }
    }

    private void ComputeEdgeMightSee(Edge source)
    {
        var sourcePlane = source.Poly.Plane;
        
        var unexploredCells = new Stack<int>();
        unexploredCells.Push(source.Destination);
        while (unexploredCells.Count > 0)
        {
            var cellIdx = unexploredCells.Pop();
            if (source.MightSee[cellIdx])
            {
                continue; // target is already explored
            }

            source.MightSee[cellIdx] = true;
            
            // Target must be partly behind source, source must be partly in front of target, and source and target cannot face each other
            foreach (var targetEdgeIdx in _graph[cellIdx].EdgeIndices)
            {
                var target = _edges[targetEdgeIdx];
                var targetPlane = target.Poly.Plane;
                
                // If we're already visited the target, target is fully behind source, or source is fully behind target
                // then we can quickly discard this portal
                if (source.MightSee[target.Destination] ||
                    MathUtils.DistanceFromNormalizedPlane(sourcePlane, target.Poly.Center) > target.Poly.Radius ||
                    MathUtils.DistanceFromNormalizedPlane(targetPlane, source.Poly.Center) < -source.Poly.Radius)
                {
                    continue;
                }

                var validTarget = false;
                foreach (var v in target.Poly.Vertices)
                {
                    if (MathUtils.DistanceFromNormalizedPlane(sourcePlane, v) < -MathUtils.Epsilon)
                    {
                        validTarget = true;
                        break;
                    }
                }
                
                if (!validTarget)
                {
                    continue;
                }
                
                validTarget = false;
                foreach (var v in source.Poly.Vertices)
                {
                    if (MathUtils.DistanceFromNormalizedPlane(targetPlane, v) > MathUtils.Epsilon)
                    {
                        validTarget = true;
                        break;
                    }
                }
                
                if (!validTarget)
                {
                    continue;
                }
                
                if (Vector3.Dot(sourcePlane.Normal, targetPlane.Normal) > MathUtils.Epsilon - 1)
                {
                    unexploredCells.Push(target.Destination);
                }
            }
        }
    }
    
    private enum Side 
    {
        Front,
        On,
        Back
    }
    
    // TODO: is this reference type poly going to fuck me?
    // TODO: Should this and Poly be in MathUtils?
    private static void ClipPolygonByPlane(ref Poly poly, Plane plane)
    {
        var vertexCount = poly.Vertices.Count;
        if (vertexCount == 0)
        {
            return;
        }
        
        // Firstly we want to tally up what side of the plane each point of the poly is on
        // This is used both to early out if nothing/everything is clipped, and to aid the clipping
        var distances = new float[vertexCount];
        var sides = new Side[vertexCount];
        var counts = new[] {0, 0, 0};
        for (var i = 0; i < vertexCount; i++)
        {
            var distance = MathUtils.DistanceFromPlane(plane, poly.Vertices[i]);
            distances[i] = distance;
            sides[i] = distance switch
            {
                > Epsilon => Side.Front,
                < -Epsilon => Side.Back,
                _ => Side.On,
            };
            counts[(int)sides[i]]++;
        }
        
        // Everything is within the half-space, so we don't need to clip anything
        if (counts[(int)Side.Back] == 0 && counts[(int)Side.On] != vertexCount)
        {
            return;
        }
        
        // Everything is outside the half-space, so we clip everything
        if (counts[(int)Side.Front] == 0)
        {
            poly.Vertices.Clear();
            return;
        }
        
        var vertices = new List<Vector3>();
        for (var i = 0; i < vertexCount; i++)
        {
            var i1 = (i + 1) % vertexCount;
            var v0 = poly.Vertices[i];
            var v1 = poly.Vertices[i1];
            var side = sides[i];
            var nextSide = sides[i1];
            
            // Vertices that are inside/on the half-space don't get clipped
            if (sides[i] != Side.Back)
            {
                vertices.Add(v0);
            }
            
            // We only need to do any clipping if we've swapped from front-to-back or vice versa
            // If either the current or next side is On then that's where we would have clipped to
            // anyway so we also don't need to do anything
            if (side == Side.On || nextSide == Side.On || side == nextSide)
            {
                continue;
            }

            // This is how far along the vector v0 -> v1 the front/back crossover occurs
            var frac = distances[i] / (distances[i] - distances[i1]);
            var splitVertex = v0 + frac * (v1 - v0);
            vertices.Add(splitVertex);
        }

        poly.Vertices = vertices;
    }
}