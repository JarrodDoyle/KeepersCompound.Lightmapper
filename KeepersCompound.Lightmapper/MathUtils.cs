using System.Numerics;

namespace KeepersCompound.Lightmapper;

public static class MathUtils
{
    public const float Epsilon = 0.001f;

    public readonly struct Aabb
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;

        public Aabb(Vector3[] points)
        {
            Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            foreach (var p in points)
            {
                Min = Vector3.Min(Min, p);
                Max = Vector3.Max(Max, p);
            }
        }
    }

    public readonly struct Sphere
    {
        public readonly Vector3 Position;
        public readonly float Radius;

        public Sphere(Vector3 position, float radius)
        {
            Position = position;
            Radius = radius;
        }
    }

    public static Vector3 ClosestPoint(Aabb aabb, Vector3 point)
    {
        return Vector3.Min(aabb.Max, Vector3.Max(aabb.Min, point));
    }

    public static bool Intersects(Sphere sphere, Aabb aabb)
    {
        var closestPoint = ClosestPoint(aabb, sphere.Position);
        var d2 = (sphere.Position - closestPoint).LengthSquared();
        var r2 = sphere.Radius * sphere.Radius;
        return d2 < r2;
    }

    public static float DistanceFromPlane(Plane plane, Vector3 point)
    {
        return Math.Abs(Vector3.Dot(plane.Normal, point) + plane.D) / plane.Normal.Length();
    }

    /// <summary>
    /// Expects poly to be convex. Given a point
    /// </summary>
    public static Vector3 ClipPointToPoly3d(Vector3 point, Vector3[] vertices, Plane projectionPlane)
    {
        // TODO: Shouldn't need to pass 3d. We can just pass the luxel coord, and then we only need the 
        var (p2d, v2ds) = LocalPlaneCoords(point, vertices, projectionPlane);

        // !HACK: Replace this shit
        var origin = vertices[0];
        var locX = vertices[1] - origin;
        var locY = Vector3.Cross(projectionPlane.Normal, locX);
        locX = Vector3.Normalize(locX);
        locY = Vector3.Normalize(locY);

        for (var i = 0; i < v2ds.Length; i++)
        {
            var a = v2ds[i];
            var b = v2ds[(i + 1) % v2ds.Length];
            var segment = b - a;
            var offset = p2d - a;
            var norm = Vector2.Normalize(new Vector2(-segment.Y, segment.X));
            var side = Vector2.Dot(norm, offset);
            if (side >= -Epsilon)
            {
                // We apply epsilon so that we push slightly into the poly. If we only
                // push to the edge then Embree sometimes misses casts. The reason
                // it's 2 epsilon is so Side == -Epsilon still gets pushed in properly
                p2d -= norm * (side + 2 * Epsilon);
            }
        }

        return origin + p2d.X * locX + p2d.Y * locY;
    }

    // TODO: Only do this once per poly
    public static (Vector2, Vector2[]) LocalPlaneCoords(Vector3 point, Vector3[] ps, Plane plane)
    {
        var origin = ps[0];
        var locX = ps[1] - origin;
        var locY = Vector3.Cross(plane.Normal, locX);

        locX = Vector3.Normalize(locX);
        locY = Vector3.Normalize(locY);

        var offset = point - origin;
        var p2d = new Vector2(Vector3.Dot(offset, locX), Vector3.Dot(offset, locY));
        var p2ds = new Vector2[ps.Length];
        for (var i = 0; i < ps.Length; i++)
        {
            var p = ps[i] - origin;
            p2ds[i] = new Vector2(Vector3.Dot(p, locX), Vector3.Dot(p, locY));
        }

        return (p2d, p2ds);
    }
}