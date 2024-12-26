using System.Numerics;
using KeepersCompound.LGS.Database.Chunks;

namespace KeepersCompound.Lightmapper;

public class Light
{
    public Vector3 Position;
    public Vector3 Color;
    public float Brightness;
    public float InnerRadius;
    public float Radius;
    public float R2;
    public bool QuadLit;

    public bool Spotlight;
    public Vector3 SpotlightDir;
    public float SpotlightInnerAngle;
    public float SpotlightOuterAngle;

    public int ObjId;
    public int LightTableIndex;
    public bool Anim;

    public WorldRep.LightTable.LightData ToLightData(float lightScale)
    {
        return new WorldRep.LightTable.LightData
        {
            Location = Position,
            Direction = SpotlightDir,
            Color = Color / lightScale,
            InnerAngle = SpotlightInnerAngle,
            OuterAngle = SpotlightOuterAngle,
            Radius = Radius == float.MaxValue ? 0 : Radius,
        };
    }

    public void FixRadius()
    {
        if (Radius == 0)
        {
            Radius = float.MaxValue;
            R2 = float.MaxValue;
        }
    }
    
    public void ApplyTransforms(
        Vector3 vhotLightPos,
        Vector3 vhotLightDir,
        Matrix4x4 translate,
        Matrix4x4 rotate,
        Matrix4x4 scale)
    {
        Position = Vector3.Transform(Position, rotate) + Vector3.Transform(vhotLightPos, scale * rotate * translate);
        SpotlightDir = Vector3.Normalize(Vector3.Transform(vhotLightDir, scale * rotate));
    }

    public float StrengthAtPoint(Vector3 point, Plane plane)
    {
        // Calculate light strength at a given point. As far as I can tell
        // this is exact to Dark (I'm a genius??). It's just an inverse distance
        // falloff with diffuse angle, except we have to scale the length.
        var dir = Position - point;
        var angle = Vector3.Dot(Vector3.Normalize(dir), plane.Normal);
        var len = dir.Length();
        var slen = len / 4.0f;
        var strength = (angle + 1.0f) / slen;

        // Inner radius starts a linear falloff to 0 at the radius
        if (InnerRadius != 0 && len > InnerRadius)
        {
            strength *= (Radius - len) / (Radius - InnerRadius);
        }

        // This is basically the same as how inner radius works. It just applies
        // a linear falloff to 0 between the inner angle and outer angle.
        if (Spotlight)
        {
            var spotAngle = Vector3.Dot(-Vector3.Normalize(dir), SpotlightDir);
            var inner = SpotlightInnerAngle;
            var outer = SpotlightOuterAngle;

            // In an improperly configured spotlight inner and outer angles might be the
            // same. So to avoid division by zero (and some clamping) we explicitly handle
            // some cases
            float spotlightMultiplier;
            if (spotAngle >= inner)
            {
                spotlightMultiplier = 1.0f;
            }
            else if (spotAngle <= outer)
            {
                spotlightMultiplier = 0.0f;
            }
            else
            {
                spotlightMultiplier = (spotAngle - outer) / (inner - outer);
            }

            strength *= spotlightMultiplier;
        }

        return strength;
    }
    
    public float CalculateMaxRadius(float minLightCutoff)
    {
        // TODO: Should it be ceiling'd? Do we need to care about hdr? (I don't think so)
        var radius = 8 * Brightness / minLightCutoff;
        return radius;
        
        // 2 / (x / 4.0f) = minLightCutoff;
        // 2 / minLightCutOff = x / 4.0f;
        // x = 8 * rgb / minLightCutOff;
    }
}