namespace KeepersCompound.LGS.Database.Chunks;

public enum SoftnessMode
{
    Standard,
    HighFourPoint,
    HighFivePoint,
    HighNinePoint,
    MediumFourPoint,
    MediumFivePoint,
    MediumNinePoint,
    LowFourPoint,
}

public class LmParams : IChunk
{
    public enum LightingMode
    {
        Quick,
        Raycast,
        Objcast,
    }
    
    public enum DepthMode
    {
        Lm16,
        Lm32,
        Lm32x,
    }

    public ChunkHeader Header { get; set; }
    public float Attenuation { get; set; }
    public float Saturation { get; set; }
    public LightingMode ShadowType { get; set; }
    public SoftnessMode ShadowSoftness { get; set; }
    public float CenterWeight { get; set; }
    public DepthMode ShadowDepth { get; set; }
    public bool LightmappedWater { get; set; }
    public int LightmapScale { get; set; }
    public uint AnimLightCutoff { get; set; }
    
    private int _dataSize;
    
    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        _dataSize = reader.ReadInt32();
        Attenuation = reader.ReadSingle();
        Saturation = reader.ReadSingle();
        ShadowType = (LightingMode)reader.ReadUInt32();
        ShadowSoftness = (SoftnessMode)reader.ReadUInt32();
        CenterWeight = reader.ReadSingle();
        ShadowDepth = (DepthMode)reader.ReadUInt32();
        LightmappedWater = reader.ReadBoolean();
        reader.ReadBytes(3);
        LightmapScale = reader.ReadInt32();
        AnimLightCutoff = reader.ReadUInt32();
    }

    public void WriteData(BinaryWriter writer)
    {
        writer.Write(_dataSize);
        writer.Write(Attenuation);
        writer.Write(Saturation);
        writer.Write((uint)ShadowType);
        writer.Write((uint)ShadowSoftness);
        writer.Write(CenterWeight);
        writer.Write((uint)ShadowDepth);
        writer.Write(LightmappedWater);
        writer.Write(new byte[3]);
        writer.Write(LightmapScale);
        writer.Write(AnimLightCutoff);
    }
}