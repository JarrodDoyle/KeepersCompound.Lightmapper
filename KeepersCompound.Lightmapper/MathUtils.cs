using System.Numerics;

namespace KeepersCompound.Lightmapper;

public static class MathUtils
{
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
}