using System.Numerics;
using KeepersCompound.LGS.Database.Chunks;

namespace KeepersCompound.Lightmapper;

public class Light
{
    public Vector3 position;
    public Vector3 color;
    public float innerRadius;
    public float radius;
    public float r2;

    public bool spotlight;
    public Vector3 spotlightDir;
    public float spotlightInnerAngle;
    public float spotlightOuterAngle;

    public int objId;
    public int lightTableIndex;
    public bool anim;

    public WorldRep.LightTable.LightData ToLightData(float lightScale)
    {
        return new WorldRep.LightTable.LightData
        {
            Location = position,
            Direction = spotlightDir,
            Color = color / lightScale,
            InnerAngle = spotlightInnerAngle,
            OuterAngle = spotlightOuterAngle,
            Radius = radius == float.MaxValue ? 0 : radius,
        };
    }
}