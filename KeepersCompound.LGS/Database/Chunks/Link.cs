
using System.Collections.Generic;
using System.IO;

namespace KeepersCompound.LGS.Database.Chunks;

public record LinkId
{
    private readonly uint _data;

    public LinkId(uint data)
    {
        _data = data;
    }

    public uint GetId()
    {
        return _data & 0xFFFF;
    }

    public bool IsConcrete()
    {
        return (_data & 0xF0000) != 0;
    }

    public uint GetRelation()
    {
        return (_data >> 20) & 0xFFF;
    }

    public uint GetRaw()
    {
        return _data;
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(_data);
    }
}

public class LinkChunk : IChunk, IMergable
{
    public record Link
    {
        public LinkId linkId;
        public int source;
        public int destination;
        public ushort relation;

        public Link(BinaryReader reader)
        {
            linkId = new LinkId(reader.ReadUInt32());
            source = reader.ReadInt32();
            destination = reader.ReadInt32();
            relation = reader.ReadUInt16();
        }

        public void Write(BinaryWriter writer)
        {
            linkId.Write(writer);
            writer.Write(source);
            writer.Write(destination);
            writer.Write(relation);
        }
    }

    public ChunkHeader Header { get; set; }
    public List<Link> links;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        links = new List<Link>();
        while (reader.BaseStream.Position < entry.Offset + entry.Size + 24)
        {
            links.Add(new Link(reader));
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        foreach (var link in links)
        {
            link.Write(writer);
        }
    }

    public void Merge(IMergable other)
    {
        links.AddRange(((LinkChunk)other).links);
    }
}

// TODO: This should be generic like Property
public class LinkDataMetaProp : IChunk, IMergable
{
    public record LinkData
    {
        public LinkId linkId;
        public int priority;

        public LinkData(BinaryReader reader)
        {
            linkId = new LinkId(reader.ReadUInt32());
            priority = reader.ReadInt32();
        }

        public void Write(BinaryWriter writer)
        {
            linkId.Write(writer);
            writer.Write(priority);
        }
    }

    public ChunkHeader Header { get; set; }
    public int DataSize;
    public List<LinkData> linkData;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        DataSize = reader.ReadInt32();
        linkData = new List<LinkData>();
        while (reader.BaseStream.Position < entry.Offset + entry.Size + 24)
        {
            linkData.Add(new LinkData(reader));
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        writer.Write(DataSize);
        foreach (var data in linkData)
        {
            data.Write(writer);
        }
    }

    public void Merge(IMergable other)
    {
        linkData.AddRange(((LinkDataMetaProp)other).linkData);
    }
}