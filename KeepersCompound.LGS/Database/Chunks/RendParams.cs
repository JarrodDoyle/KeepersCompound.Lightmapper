using System.Numerics;

namespace KeepersCompound.LGS.Database.Chunks;

public enum SunlightMode
{
    SingleUnshadowed,
    QuadObjcastShadows,
    QuadUnshadowed,
    SingleObjcastShadows
}

public class RendParams : IChunk
{
    public ChunkHeader Header { get; set; }

    public string Palette;
    public Vector3 AmbientLight;
    public bool UseSunlight;
    public SunlightMode SunlightMode;
    public Vector3 SunlightDirection;
    public float SunlightHue;
    public float SunlightSaturation;
    public float SunlightBrightness;
    public float ViewDistance;
    public Vector3[] AmbientLightZones;
    public float GlobalAiVisBias;
    public float[] AmbientZoneAiVisBiases;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        Palette = reader.ReadNullString(16);
        AmbientLight = reader.ReadVec3();
        UseSunlight = reader.ReadBoolean();
        reader.ReadBytes(3);
        SunlightMode = (SunlightMode)reader.ReadUInt32();
        SunlightDirection = reader.ReadVec3();
        SunlightHue = reader.ReadSingle();
        SunlightSaturation = reader.ReadSingle();
        SunlightBrightness = reader.ReadSingle();
        reader.ReadBytes(24);
        ViewDistance = reader.ReadSingle();
        reader.ReadBytes(12);
        AmbientLightZones = new Vector3[8];
        for (var i = 0; i < AmbientLightZones.Length; i++)
        {
            AmbientLightZones[i] = reader.ReadVec3();
        }

        GlobalAiVisBias = reader.ReadSingle();
        AmbientZoneAiVisBiases = new float[8];
        for (var i = 0; i < AmbientZoneAiVisBiases.Length; i++)
        {
            AmbientZoneAiVisBiases[i] = reader.ReadSingle();
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        writer.WriteNullString(Palette, 16);
        writer.WriteVec3(AmbientLight);
        writer.Write(UseSunlight);
        writer.Write(new byte[3]);
        writer.Write((uint)SunlightMode);
        writer.WriteVec3(SunlightDirection);
        writer.Write(SunlightHue);
        writer.Write(SunlightSaturation);
        writer.Write(SunlightBrightness);
        writer.Write(new byte[24]);
        writer.Write(ViewDistance);
        writer.Write(new byte[12]);
        foreach (var lightZone in AmbientLightZones)
        {
            writer.WriteVec3(lightZone);
        }

        writer.Write(GlobalAiVisBias);
        foreach (var visBias in AmbientZoneAiVisBiases)
        {
            writer.Write(visBias);
        }
    }
}