namespace KeepersCompound.LGS.Database;

public struct Version
{
    public uint Major { get; set; }
    public uint Minor { get; set; }

    public Version(BinaryReader reader)
    {
        Major = reader.ReadUInt32();
        Minor = reader.ReadUInt32();
    }

    public readonly void Write(BinaryWriter writer)
    {
        writer.Write(Major);
        writer.Write(Minor);
    }

    public override readonly string ToString()
    {
        return $"{Major}.{Minor}";
    }
}