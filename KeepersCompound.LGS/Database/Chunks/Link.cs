using Serilog;

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

public class LinkChunk : IChunk, IMergeable
{
    public record Link
    {
        public LinkId LinkId;
        public int Source;
        public int Destination;
        public ushort Relation;

        public Link(BinaryReader reader)
        {
            LinkId = new LinkId(reader.ReadUInt32());
            Source = reader.ReadInt32();
            Destination = reader.ReadInt32();
            Relation = reader.ReadUInt16();
        }

        public void Write(BinaryWriter writer)
        {
            LinkId.Write(writer);
            writer.Write(Source);
            writer.Write(Destination);
            writer.Write(Relation);
        }
    }

    public ChunkHeader Header { get; set; }
    public List<Link> Links;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        Links = new List<Link>();
        while (reader.BaseStream.Position < entry.Offset + entry.Size + 24)
        {
            Links.Add(new Link(reader));
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        foreach (var link in Links)
        {
            link.Write(writer);
        }
    }

    public void Merge(IMergeable other)
    {
        // !HACK: We always merge into gamesys so we can pre-trim garbage here
        var count = Links.Count;
        for (var i = count - 1; i >= 0; i--)
        {
            var link = Links[i];
            if (link.LinkId.IsConcrete())
            {
                Links.RemoveAt(i);
            }
        }

        if (Links.Count != count)
        {
            Log.Information("Trimming excess Links in GAM: {StartCount} -> {EndCount}", count, Links.Count);
        }

        Links.AddRange(((LinkChunk)other).Links);
    }
}

// TODO: This should be generic like Property
public class LinkDataMetaProp : IChunk, IMergeable
{
    public record LinkData
    {
        public LinkId LinkId;
        public int Priority;

        public LinkData(BinaryReader reader)
        {
            LinkId = new LinkId(reader.ReadUInt32());
            Priority = reader.ReadInt32();
        }

        public void Write(BinaryWriter writer)
        {
            LinkId.Write(writer);
            writer.Write(Priority);
        }
    }

    public ChunkHeader Header { get; set; }
    public int DataSize;
    public List<LinkData> LinkDatas;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        DataSize = reader.ReadInt32();
        LinkDatas = new List<LinkData>();
        while (reader.BaseStream.Position < entry.Offset + entry.Size + 24)
        {
            LinkDatas.Add(new LinkData(reader));
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        writer.Write(DataSize);
        foreach (var data in LinkDatas)
        {
            data.Write(writer);
        }
    }

    public void Merge(IMergeable other)
    {
        // !HACK: We always merge into gamesys so we can pre-trim garbage here
        var count = LinkDatas.Count;
        for (var i = count - 1; i >= 0; i--)
        {
            var link = LinkDatas[i];
            if (link.LinkId.IsConcrete())
            {
                LinkDatas.RemoveAt(i);
            }
        }

        if (LinkDatas.Count != count)
        {
            Log.Information("Trimming excess LinkData in GAM: {StartCount} -> {EndCount}", count, LinkDatas.Count);
        }

        LinkDatas.AddRange(((LinkDataMetaProp)other).LinkDatas);
    }
}