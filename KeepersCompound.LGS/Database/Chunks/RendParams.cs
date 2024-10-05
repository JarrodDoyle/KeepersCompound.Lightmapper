using System.Numerics;

namespace KeepersCompound.LGS.Database.Chunks;

public class RendParams : IChunk
{
    public enum SunlightMode
    {
        SingleUnshadowed,
        QuadObjcastShadows,
        QuadUnshadowed,
        SingleObjcastShadows,
    }

    public ChunkHeader Header { get; set; }

    public string palette;
    public Vector3 ambientLight;
    public int useSunlight;
    public SunlightMode sunlightMode;
    public Vector3 sunlightDirection;
    public float sunlightHue;
    public float sunlightSaturation;
    public float sunlightBrightness;
    public float viewDistance;
    public Vector3[] ambientLightZones;
    public float globalAiVisBias;
    public float[] ambientZoneAiVisBiases;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        palette = reader.ReadNullString(16);
        ambientLight = reader.ReadVec3();
        useSunlight = reader.ReadInt32();
        sunlightMode = (SunlightMode)reader.ReadUInt32();
        sunlightDirection = reader.ReadVec3();
        sunlightHue = reader.ReadSingle();
        sunlightSaturation = reader.ReadSingle();
        sunlightBrightness = reader.ReadSingle();
        reader.ReadBytes(24);
        viewDistance = reader.ReadSingle();
        reader.ReadBytes(12);
        ambientLightZones = new Vector3[8];
        for (var i = 0; i < ambientLightZones.Length; i++)
        {
            ambientLightZones[i] = reader.ReadVec3();
        }
        globalAiVisBias = reader.ReadSingle();
        ambientZoneAiVisBiases = new float[8];
        for (var i = 0; i < ambientZoneAiVisBiases.Length; i++)
        {
            ambientZoneAiVisBiases[i] = reader.ReadSingle();
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        writer.WriteNullString(palette, 16);
        writer.WriteVec3(ambientLight);
        writer.Write(useSunlight);
        writer.Write((uint)sunlightMode);
        writer.WriteVec3(sunlightDirection);
        writer.Write(sunlightHue);
        writer.Write(sunlightSaturation);
        writer.Write(sunlightBrightness);
        writer.Write(new byte[24]);
        writer.Write(viewDistance);
        writer.Write(new byte[12]);
        foreach (var lightZone in ambientLightZones)
        {
            writer.WriteVec3(lightZone);
        }
        writer.Write(globalAiVisBias);
        foreach (var visBias in ambientZoneAiVisBiases)
        {
            writer.Write(visBias);
        }
    }
}