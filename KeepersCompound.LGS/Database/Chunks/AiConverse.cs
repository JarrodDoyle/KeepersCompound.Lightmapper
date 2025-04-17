namespace KeepersCompound.LGS.Database.Chunks;

public class AiConverseChunk : IChunk
{
    public ChunkHeader Header { get; set; }
    public uint Count;
    public uint[] Ids;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        Count = reader.ReadUInt32();
        Ids = new uint[Count];
        for (var i = 0; i < Count; i++)
        {
            Ids[i] = reader.ReadUInt32();
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        writer.Write(Count);
        for (var i = 0; i < Count; i++)
        {
            writer.Write(Ids[i]);
        }
    }
}