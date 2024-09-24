using System.Numerics;

namespace KeepersCompound.Lightmapper;

public static class MathUtils
{
    public static float DistanceFromPlane(Plane plane, Vector3 point)
    {
        return Math.Abs(Vector3.Dot(plane.Normal, point) + plane.D) / plane.Normal.Length();
    }
}