using System.Numerics;

namespace KeepersCompound.Lightmapper;

public static class Utils
{
    // Expects Hue and Saturation are 0-1, Brightness 0-255
    // https://en.wikipedia.org/wiki/HSL_and_HSV#HSV_to_RGB
    public static Vector3 HsbToRgb(float hue, float saturation, float brightness)
    {
        hue *= 360;
        var hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        var f = hue / 60 - Math.Floor(hue / 60);

        var v = Convert.ToInt32(brightness);
        var p = Convert.ToInt32(brightness * (1 - saturation));
        var q = Convert.ToInt32(brightness * (1 - f * saturation));
        var t = Convert.ToInt32(brightness * (1 - (1 - f) * saturation));

        return hi switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }
}

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

    // Should automagically handle max float radii
    public static bool Intersects(Sphere sphere, Sphere other)
    {
        var rsum = sphere.Radius + other.Radius;
        return (sphere.Position - other.Position).Length() <= rsum;
    }

    public static float DistanceFromPlane(Plane plane, Vector3 point)
    {
        return Math.Abs(Vector3.Dot(plane.Normal, point) + plane.D) / plane.Normal.Length();
    }

    public record PlanePointMapper
    {
        Vector3 _origin;
        Vector3 _xAxis;
        Vector3 _yAxis;

        public PlanePointMapper(Vector3 normal, Vector3 p0, Vector3 p1)
        {
            _origin = p0;
            _xAxis = p1 - _origin;
            _yAxis = Vector3.Cross(normal, _xAxis);

            _xAxis = Vector3.Normalize(_xAxis);
            _yAxis = Vector3.Normalize(_yAxis);
        }

        public Vector2 MapTo2d(Vector3 point)
        {
            var offset = point - _origin;
            var x = Vector3.Dot(offset, _xAxis);
            var y = Vector3.Dot(offset, _yAxis);
            return new Vector2(x, y);
        }

        public Vector2[] MapTo2d(Vector3[] points)
        {
            var points2d = new Vector2[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                points2d[i] = MapTo2d(points[i]);
            }
            return points2d;
        }

        public Vector3 MapTo3d(Vector2 point)
        {
            return _origin + point.X * _xAxis + point.Y * _yAxis;
        }

        public Vector3[] MapTo3d(Vector2[] points)
        {
            var points3d = new Vector3[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                points3d[i] = MapTo3d(points[i]);
            }
            return points3d;
        }
    }

    public static Vector2 ClipPointToPoly2d(Vector2 point, Vector2[] vertices)
    {
        var vertexCount = vertices.Length;
        for (var i = 0; i < vertexCount; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertexCount];
            var segment = b - a;
            var offset = point - a;
            var norm = Vector2.Normalize(new Vector2(-segment.Y, segment.X));
            var side = Vector2.Dot(norm, offset);
            if (side >= -Epsilon)
            {
                // We apply epsilon so that we push slightly into the poly. If we only
                // push to the edge then Embree sometimes misses casts. The reason
                // it's 2 epsilon is so Side == -Epsilon still gets pushed in properly
                point -= norm * (side + 2 * Epsilon);
            }
        }

        return point;
    }
}