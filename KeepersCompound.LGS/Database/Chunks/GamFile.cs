using System;
using System.IO;
using System.Text;

namespace KeepersCompound.LGS.Database.Chunks;

public class GamFile : IChunk
{
    public ChunkHeader Header { get; set; }
    public string fileName;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        fileName = reader.ReadNullString(256);
    }

    public void WriteData(BinaryWriter writer)
    {
        throw new System.NotImplementedException();
    }
}