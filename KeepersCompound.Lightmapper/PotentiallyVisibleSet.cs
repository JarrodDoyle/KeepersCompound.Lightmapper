using System.Numerics;
using KeepersCompound.LGS.Database.Chunks;

namespace KeepersCompound.Lightmapper;

public class PotentiallyVisibleSet
{
    private class Edge
    {
        public int Left;
        public int Right;
        public Poly Poly;
    }

    private class Poly(Vector3[] vertices, Plane plane)
    {
        public Vector3[] Vertices = vertices;
        public Plane Plane = plane;

        public bool IsCoplanar(Poly other)
        {
            // TODO: should this be in mathutils?
            const float e = MathUtils.Epsilon;
            var m = Plane.D / other.Plane.D;

            var n0 = Plane.Normal;
            var n1 = other.Plane.Normal * m;
            return Math.Abs(n0.X - n1.X) < e && Math.Abs(n0.Y - n1.Y) < e && Math.Abs(n0.Z - n1.Z) < e;
        }
    }
    
    private readonly List<int>[] _portalGraph;
    private readonly List<Edge> _edges;
    private readonly Dictionary<int, HashSet<int>> _visibilitySet;

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
        _edges = [];
        _visibilitySet = new Dictionary<int, HashSet<int>>();
        
        // TODO: Ignore blocksvision portals
        _portalGraph = new List<int>[cells.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            _portalGraph[i] = [];
            var cell = cells[i];

            // If a cell is "blocks vision" flagged, we can never see out of it
            // We can see into it though, so we still want the edges coming in
            if ((cell.Flags & 8) != 0)
            {
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
                
                var other = poly.Destination;
                
                // If there's already an existing edge between the two cells then we just need to add a reference to it
                // otherwise we need to actually build the edge
                var edgeIndex = _edges.FindIndex(e => (e.Left == i && e.Right == other) || (e.Left == other && e.Right == i));
                if (edgeIndex == -1)
                {
                    var vs = new Vector3[poly.VertexCount];
                    for (var vIdx = 0; vIdx < poly.VertexCount; vIdx++)
                    {
                        vs[vIdx] = cell.Vertices[cell.Indices[indicesOffset + vIdx]];
                    }
                    
                    var edge = new Edge
                    {
                        Left = i,
                        Right = other,
                        Poly = new Poly(vs, cell.Planes[poly.PlaneId]),
                    };
                    _edges.Add(edge);
                    edgeIndex = _edges.Count - 1;
                }

                _portalGraph[i].Add(edgeIndex);
                indicesOffset += poly.VertexCount;
            }
        }
    }

    public int[] GetVisible(int cellIdx)
    {
        if (_visibilitySet.TryGetValue(cellIdx, out var value))
        {
            return [..value];
        }

        var visibleCells = ComputeVisibility(cellIdx);
        _visibilitySet.Add(cellIdx, visibleCells);
        return [..visibleCells];
    }
    
    private HashSet<int> ComputeVisibility(int cellIdx)
    {
        if (cellIdx >= _portalGraph.Length)
        {
            return [];
        }

        // A cell can always see itself, so we'll add that now
        var visible = new HashSet<int>();
        visible.Add(cellIdx);

        // Additionally a cell can always see it's direct neighbours (obviously)
        foreach (var edgeIndex in _portalGraph[cellIdx])
        {
            var edge = _edges[edgeIndex];
            var neighbourIdx = edge.Left == cellIdx ? edge.Right : edge.Left;
            visible.Add(neighbourIdx);
            
            // Neighbours of our direct neighbour are always visible, unless they're coplanar
            foreach (var innerEdgeIndex in _portalGraph[neighbourIdx])
            {
                var innerEdge = _edges[innerEdgeIndex];
                var leadsBack = innerEdge.Left == cellIdx || innerEdge.Right == cellIdx;
                if (leadsBack || edge.Poly.IsCoplanar(innerEdge.Poly))
                {
                    continue;
                }

                // Now we get to the recursive section
                var destination = innerEdge.Left == neighbourIdx ? innerEdge.Right : innerEdge.Left;
                ComputeClippedVisibility(visible, edge.Poly, innerEdge.Poly, neighbourIdx, destination, 0);
            }
        }
        
        return visible;
    }
    
    // TODO: Name this better
    // TODO: This *should* be poly's not edges
    private void ComputeClippedVisibility(
        HashSet<int> visible,
        Poly sourcePoly,
        Poly previousPoly,
        int previousCellIdx,
        int currentCellIdx,
        int depth)
    {
        if (depth > 2048)
        {
            return;
        }
        
        visible.Add(currentCellIdx);

        // Generate separating planes
        var separators = new List<Plane>();
        separators.AddRange(GetSeparatingPlanes(sourcePoly, previousPoly, false));
        separators.AddRange(GetSeparatingPlanes(previousPoly, sourcePoly, true));
        
        // Clip all new polys and recurse
        foreach (var edgeIndex in _portalGraph[currentCellIdx])
        {
            var edge = _edges[edgeIndex];
            var destination = edge.Left == previousCellIdx ? edge.Right : edge.Left;
            if (destination == previousCellIdx || previousPoly.IsCoplanar(edge.Poly))
            {
                continue;
            }

            var poly = separators.Aggregate(edge.Poly, ClipPolygonByPlane);
            if (poly.Vertices.Length == 0)
            {
                continue;
            }
            
            ComputeClippedVisibility(visible, sourcePoly, poly, currentCellIdx, destination, depth + 1);
        }
    }

    private static List<Plane> GetSeparatingPlanes(Poly p0, Poly p1, bool flip)
    {
        var separators = new List<Plane>();
        for (var i = 0; i < p0.Vertices.Length; i++)
        {
            // brute force all combinations
            // there's probably some analytical way to choose the "correct" v2 but I couldn't find anything online
            var v0 = p0.Vertices[i];
            var v1 = p0.Vertices[(i + 1) % p0.Vertices.Length];
            for (var j = 0; j < p1.Vertices.Length; j++)
            {
                var v2 = p1.Vertices[j];
                
                var normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
                var d = Vector3.Dot(v2, normal);
                var plane = new Plane(normal, d);
                
                // Depending on how the edges were built, the resulting plane might be facing the wrong way
                if (MathUtils.DistanceFromPlane(p0.Plane, v2) < MathUtils.Epsilon)
                {
                    plane.Normal = -plane.Normal;
                    plane.D = -plane.D;
                }
                
                // All points should be behind/on the plane
                var count = 0;
                for (var k = 0; k < p1.Vertices.Length; k++)
                {
                    if (k == j || MathUtils.DistanceFromPlane(plane, p1.Vertices[k]) > MathUtils.Epsilon)
                    {
                        count++;
                    }
                }

                if (count != p1.Vertices.Length)
                {
                    continue;
                }

                if (flip)
                {
                    plane.Normal = -plane.Normal;
                    plane.D = -plane.D;
                }
                
                separators.Add(plane);
            }
        }

        return separators;
    }
    
    private enum Side 
    {
        Front,
        On,
        Back
    }
    
    // TODO: is this reference type poly going to fuck me?
    // TODO: Should this and Poly be in MathUtils?
    private static Poly ClipPolygonByPlane(Poly poly, Plane plane)
    {
        var vertexCount = poly.Vertices.Length;
        
        // Firstly we want to tally up what side of the plane each point of the poly is on
        // This is used both to early out if nothing/everything is clipped, and to aid the clipping
        var distances = new float[vertexCount];
        var sides = new Side[vertexCount];
        var counts = new int[3];
        for (var i = 0; i < vertexCount; i++)
        {
            var distance = MathUtils.DistanceFromPlane(plane, poly.Vertices[i]);
            distances[i] = distance;
            sides[i] = distance switch {
                > MathUtils.Epsilon => Side.Front,
                <-MathUtils.Epsilon => Side.Back,
                _ => Side.On,
            };
            counts[(int)sides[i]]++;
        }

        // Everything is within the half-space, so we don't need to clip anything
        if (counts[(int)Side.Back] == 0)
        {
            return new Poly(poly.Vertices, poly.Plane);
        }
        
        // Everything is outside the half-space, so we clip everything
        if (counts[(int)Side.Back] == vertexCount)
        {
            return new Poly([], poly.Plane);
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
            if (side == Side.On || nextSide == Side.On || side != nextSide)
            {
                continue;
            }

            // This is how far along the vector v0 -> v1 the front/back crossover occurs
            var frac = distances[i] / (distances[i] - distances[i1]);
            var splitVertex = v0 + frac * (v1 - v0);
            vertices.Add(splitVertex);
        }
        
        return new Poly([..vertices], poly.Plane);
    }
}