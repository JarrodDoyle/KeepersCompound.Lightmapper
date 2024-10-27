using System.Numerics;
using KeepersCompound.LGS.Database.Chunks;

namespace KeepersCompound.Lightmapper;

public class Light
{
    public Vector3 Position;
    public Vector3 Color;
    public float InnerRadius;
    public float Radius;
    public float R2;

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
}