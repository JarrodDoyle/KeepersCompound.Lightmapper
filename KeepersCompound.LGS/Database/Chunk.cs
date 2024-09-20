using System;
using System.IO;
using System.Text;

namespace KeepersCompound.LGS.Database;

public struct ChunkHeader
{
    public string Name { get; set; }
    public Version Version { get; set; }

    public ChunkHeader(BinaryReader reader)
    {
        Name = reader.ReadNullString(12);
        Version = new(reader);
        reader.ReadBytes(4);
    }

    public readonly void Write(BinaryWriter writer)
    {
        var writeBytes = new byte[12];
        var nameBytes = Encoding.UTF8.GetBytes(Name);
        nameBytes[..Math.Min(12, nameBytes.Length)].CopyTo(writeBytes, 0);
        writer.Write(writeBytes);
        Version.Write(writer);
        writer.Write(new byte[4]);
    }
}

public interface IChunk
{
    public ChunkHeader Header { get; set; }

    public void Read(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        reader.BaseStream.Seek(entry.Offset, SeekOrigin.Begin);

        Header = new(reader);
        ReadData(reader, entry);
    }

    public void Write(BinaryWriter writer)
    {
        Header.Write(writer);
        WriteData(writer);
    }

    public abstract void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry);
    public abstract void WriteData(BinaryWriter writer);
}

public class GenericChunk : IChunk
{
    public ChunkHeader Header { get; set; }
    public byte[] Data { get; set; }

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        Data = reader.ReadBytes((int)entry.Size);
    }

    public void WriteData(BinaryWriter writer)
    {
        writer.Write(Data);
    }
}

public interface IMergable
{
    public abstract void Merge(IMergable other);
}