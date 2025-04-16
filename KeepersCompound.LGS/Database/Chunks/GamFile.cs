namespace KeepersCompound.LGS.Database.Chunks;

public class GamFile : IChunk
{
    public ChunkHeader Header { get; set; }
    public string FileName;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        FileName = reader.ReadNullString(256);
    }

    public void WriteData(BinaryWriter writer)
    {
        writer.WriteNullString(FileName, 256);
    }
}