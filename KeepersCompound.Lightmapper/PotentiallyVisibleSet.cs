using System.Numerics;
using KeepersCompound.LGS.Database.Chunks;

namespace KeepersCompound.Lightmapper;

public class PotentiallyVisibleSet
{
    private struct Node(List<int> edgeIndices)
    {
        public bool VisibilityComputed = false;
        public HashSet<int> VisibleNodes = [];
        public readonly List<int> EdgeIndices = edgeIndices;
    }
    
    private readonly struct Edge(int destination, Poly poly)
    {
        public readonly HashSet<int> MightSee = [];
        public readonly int Destination = destination;
        public readonly Poly Poly = poly;

        public override string ToString()
        {
            return $"<Destination: {Destination}, Poly: {Poly}";
        }
    }

    private struct Poly
    {
        public List<Vector3> Vertices;
        public readonly Plane Plane;

        public Poly(List<Vector3> vertices, Plane plane)
        {
            Vertices = vertices;
            Plane = plane;
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

    private const float Epsilon = 0.1f;
    
    // This is yucky and means we're not thread safe
    private readonly List<float> _clipDistances = new(32);
    private readonly List<Side> _clipSides = new(32);
    private readonly int[] _clipCounts = [0, 0, 0];

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
        _edges = [];
        
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

                var edge = new Edge(poly.Destination, new Poly(vs, cell.Planes[poly.PlaneId]));
                edgeIndices.Add(_edges.Count);
                _edges.Add(edge);
                indicesOffset += poly.VertexCount;
            }

            _graph[i] = new Node(edgeIndices);
        }

        Parallel.For(0, _edges.Count, ComputeEdgeMightSee);
    }

    public int[] GetVisible(int cellIdx)
    {
        // TODO: Handle out of range indices
        var node = _graph[cellIdx];
        if (node.VisibilityComputed)
        {
            return [..node.VisibleNodes];
        }

        var visibleCells = ComputeVisibility(cellIdx);
        node.VisibilityComputed = true;
        node.VisibleNodes = visibleCells;
        _graph[cellIdx] = node;
        return [..visibleCells];
    }

    
    private void ComputeEdgeMightSee(int edgeIdx)
    {
        var source = _edges[edgeIdx];
        var sourcePlane = source.Poly.Plane;
        Flood(source.Destination);
        return;

        void Flood(int cellIdx)
        {
            if (!source.MightSee.Add(cellIdx))
            {
                return; // target is already explored
            }
            
            // Target must be partly behind source, source must be partly in front of target, and source and target cannot face each other
            foreach (var targetEdgeIdx in _graph[cellIdx].EdgeIndices)
            {
                var target = _edges[targetEdgeIdx];
                var targetPlane = target.Poly.Plane;

                var validTarget = false;
                foreach (var v in target.Poly.Vertices)
                {
                    if (MathUtils.DistanceFromPlane(sourcePlane, v) < -MathUtils.Epsilon)
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
                    if (MathUtils.DistanceFromPlane(targetPlane, v) > MathUtils.Epsilon)
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
                    Flood(target.Destination);
                }
            }
        }
    }
    
    private HashSet<int> ComputeVisibility(int cellIdx)
    {
        if (cellIdx >= _graph.Length)
        {
            return [];
        }

        // A cell can always see itself, so we'll add that now
        var visible = new HashSet<int>();
        visible.Add(cellIdx);

        foreach (var edgeIdx in _graph[cellIdx].EdgeIndices)
        {
            var edge = _edges[edgeIdx];
            foreach (var mightSee in edge.MightSee)
            {
                visible.Add(mightSee);
            }
        }
        
        return visible;
        
        // if (cellIdx >= _portalGraph.Length)
        // {
        //     return [];
        // }

        // Additionally a cell can always see it's direct neighbours (obviously)
        // foreach (var edgeIndex in _portalGraph[cellIdx])
        // {
        //     var edge = _edges[edgeIndex];
        //     var neighbourIdx = edge.Destination;
        //     visible.Add(neighbourIdx);
        //     
        //     // Neighbours of our direct neighbour are always visible, unless they're coplanar
        //     foreach (var innerEdgeIndex in _portalGraph[neighbourIdx])
        //     {
        //         var innerEdge = _edges[innerEdgeIndex];
        //         if (innerEdge.Destination == cellIdx || edge.Poly.IsCoplanar(innerEdge.Poly))
        //         {
        //             continue;
        //         }
        //
        //         ExplorePortalRecursive(visible, edge.Poly, new Poly(innerEdge.Poly), neighbourIdx, innerEdge.Destination, 0);
        //     }
        // }
        
        // return visible;
    }
    
    // private void ExplorePortalRecursive(
    //     HashSet<int> visible,
    //     Poly sourcePoly,
    //     Poly previousPoly,
    //     int previousCellIdx,
    //     int currentCellIdx,
    //     int depth)
    // {
    //     // TODO: Might need to lose this
    //     if (depth > 1024)
    //     {
    //         return;
    //     }
    //     
    //     visible.Add(currentCellIdx);
    //     
    //     // Only one edge out of the cell means we'd be going back on ourselves
    //     if (_portalGraph[currentCellIdx].Count <= 1)
    //     {
    //         return;
    //     }
    //     
    //     // TODO: If all neighbours are already in `visible` skip exploring?
    //     
    //     var separators = new List<Plane>();
    //     GetSeparatingPlanes(separators, sourcePoly, previousPoly, false);
    //     GetSeparatingPlanes(separators, previousPoly, sourcePoly, true);
    //
    //     // The case for this occuring is... interesting ( idk )
    //     if (separators.Count == 0)
    //     {
    //         return;
    //     }
    //     
    //     // Clip all new polys and recurse
    //     foreach (var edgeIndex in _portalGraph[currentCellIdx])
    //     {
    //         var edge = _edges[edgeIndex];
    //         if (edge.Destination == previousCellIdx || previousPoly.IsCoplanar(edge.Poly) || sourcePoly.IsCoplanar(edge.Poly))
    //         {
    //             continue;
    //         }
    //
    //         var poly = new Poly(edge.Poly);
    //         foreach (var separator in separators)
    //         {
    //             ClipPolygonByPlane(ref poly, separator);
    //         }
    //         
    //         if (poly.Vertices.Count == 0)
    //         {
    //             continue;
    //         }
    //         
    //         ExplorePortalRecursive(visible, sourcePoly, poly, currentCellIdx, edge.Destination, depth + 1);
    //     }
    // }

    // TODO: We're getting multiple separating planes that are the same, let's not somehow?
    private static void GetSeparatingPlanes(List<Plane> separators, Poly p0, Poly p1, bool flip)
    {
        for (var i = 0; i < p0.Vertices.Count; i++)
        {
            // brute force all combinations
            // there's probably some analytical way to choose the "correct" v2 but I couldn't find anything online
            var v0 = p0.Vertices[i];
            var v1 = p0.Vertices[(i + 1) % p0.Vertices.Count];
            for (var j = 0; j < p1.Vertices.Count; j++)
            {
                var v2 = p1.Vertices[j];
                
                var normal = Vector3.Cross(v1 - v0, v2 - v0);
                if (normal.LengthSquared() < Epsilon)
                {
                    // colinear (or near colinear) points will produce an invalid plane
                    continue;
                }
                
                normal = Vector3.Normalize(normal);
                var d = -Vector3.Dot(v2, normal);
                
                // Depending on how the edges were built, the resulting plane might be facing the wrong way
                var distanceToSource = MathUtils.DistanceFromPlane(p0.Plane, v2);
                if (distanceToSource > Epsilon)
                {
                    normal = -normal;
                    d = -d;
                }
                
                var plane = new Plane(normal, d);
                
                if (MathUtils.IsCoplanar(plane, flip ? p0.Plane : p1.Plane))
                {
                    continue;
                }
                
                // All points should be in front of the plane (except for the point used to create it)
                var invalid = false;
                var count = 0;
                for (var k = 0; k < p1.Vertices.Count; k++)
                {
                    if (k == j)
                    {
                        continue;
                    }
                    
                    var dist = MathUtils.DistanceFromPlane(plane, p1.Vertices[k]);
                    if (dist > Epsilon)
                    {
                        count++;
                    }
                    else if (dist < -Epsilon)
                    {
                        invalid = true;
                        break;
                    }
                }

                if (invalid || count == 0)
                {
                    continue;
                }

                if (flip)
                {
                    plane.Normal = -normal;
                    plane.D = -d;
                }
                
                separators.Add(plane);
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
    private void ClipPolygonByPlane(ref Poly poly, Plane plane)
    {
        var vertexCount = poly.Vertices.Count;
        if (vertexCount == 0)
        {
            return;
        }
        
        // Firstly we want to tally up what side of the plane each point of the poly is on
        // This is used both to early out if nothing/everything is clipped, and to aid the clipping
        // var distances = new float[vertexCount];
        // var sides = new Side[vertexCount];
        // var counts = new int[3];
        _clipDistances.Clear();
        _clipSides.Clear();
        _clipCounts[0] = 0;
        _clipCounts[1] = 0;
        _clipCounts[2] = 0;
        for (var i = 0; i < vertexCount; i++)
        {
            var distance = MathUtils.DistanceFromPlane(plane, poly.Vertices[i]);
            _clipDistances.Add(distance);
            _clipSides.Add(distance switch {
                > Epsilon => Side.Front,
                <-Epsilon => Side.Back,
                _ => Side.On,
            });
            _clipCounts[(int)_clipSides[i]]++;
        }

        // Everything is within the half-space, so we don't need to clip anything
        if (_clipCounts[(int)Side.Back] == 0 && _clipCounts[(int)Side.On] != vertexCount)
        {
            return;
        }
        
        // Everything is outside the half-space, so we clip everything
        if (_clipCounts[(int)Side.Front] == 0)
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
            var side = _clipSides[i];
            var nextSide = _clipSides[i1];
            
            // Vertices that are inside/on the half-space don't get clipped
            if (_clipSides[i] != Side.Back)
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
            var frac = _clipDistances[i] / (_clipDistances[i] - _clipDistances[i1]);
            var splitVertex = v0 + frac * (v1 - v0);
            vertices.Add(splitVertex);
        }

        poly.Vertices = vertices;
    }
}