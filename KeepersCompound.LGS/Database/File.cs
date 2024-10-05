
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KeepersCompound.LGS.Database.Chunks;

namespace KeepersCompound.LGS.Database;

public class DbFile
{
    public struct FHeader
    {
        public uint TocOffset { get; set; }
        public Version Version { get; }
        public string Deadbeef { get; }

        public FHeader(BinaryReader reader)
        {
            TocOffset = reader.ReadUInt32();
            Version = new Version(reader);
            reader.ReadBytes(256);
            Deadbeef = BitConverter.ToString(reader.ReadBytes(4));
        }

        public readonly void Write(BinaryWriter writer)
        {
            writer.Write(TocOffset);
            Version.Write(writer);
            writer.Write(new byte[256]);
            writer.Write(Array.ConvertAll(Deadbeef.Split('-'), s => byte.Parse(s, System.Globalization.NumberStyles.HexNumber)));
        }
    }

    public struct TableOfContents
    {
        public struct Entry
        {
            public string Name;
            public uint Offset;
            public uint Size;

            public Entry(BinaryReader reader)
            {
                Name = reader.ReadNullString(12);
                Offset = reader.ReadUInt32();
                Size = reader.ReadUInt32();
            }

            public override readonly string ToString()
            {
                return $"Name: {Name}, Offset: {Offset}, Size: {Size}";
            }

            public readonly void Write(BinaryWriter writer)
            {
                writer.WriteNullString(Name, 12);
                writer.Write(Offset);
                writer.Write(Size);
            }
        }

        public uint ItemCount { get; }
        public List<Entry> Items { get; }

        public TableOfContents(BinaryReader reader)
        {
            ItemCount = reader.ReadUInt32();
            Items = new List<Entry>();
            for (var i = 0; i < ItemCount; i++)
                Items.Add(new Entry(reader));
            Items.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        }

        public readonly void Write(BinaryWriter writer)
        {
            writer.Write(ItemCount);
            foreach (var entry in Items)
            {
                entry.Write(writer);
            }
        }
    }

    public FHeader Header { get; private set; }
    public TableOfContents Toc { get; }
    public Dictionary<string, IChunk> Chunks { get; set; }

    public DbFile(string filename)
    {
        // TODO: Throw rather than return
        if (!File.Exists(filename)) return;

        using MemoryStream stream = new(File.ReadAllBytes(filename));
        using BinaryReader reader = new(stream, Encoding.UTF8, false);

        Header = new(reader);
        stream.Seek(Header.TocOffset, SeekOrigin.Begin);
        Toc = new(reader);

        Chunks = new Dictionary<string, IChunk>();
        foreach (var entry in Toc.Items)
        {
            var chunk = NewChunk(entry.Name);
            chunk.Read(reader, entry);
            Chunks.Add(entry.Name, chunk);
        }
    }

    public void Save(string filename)
    {
        using var stream = File.Open(filename, FileMode.Create);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, false);

        Header.Write(writer);
        for (var i = 0; i < Toc.ItemCount; i++)
        {
            var item = Toc.Items[i];
            var pos = stream.Position;

            var chunk = Chunks[item.Name];
            chunk.Write(writer);
            var size = stream.Position - pos - 24;

            item.Offset = (uint)pos;
            item.Size = (uint)size;
            Toc.Items[i] = item;
        }
        var tocOffset = (uint)stream.Position;
        Toc.Write(writer);

        stream.Seek(0, SeekOrigin.Begin);
        writer.Write(tocOffset);
    }

    public bool TryGetChunk<T>(string name, out T chunk)
    {
        if (Chunks.TryGetValue(name, out var rawChunk))
        {
            chunk = (T)rawChunk;
            return true;
        }
        chunk = default;
        return false;
    }

    private static IChunk NewChunk(string entryName)
    {
        return entryName switch
        {
            // "AI_ROOM_DB" => new AiRoomDb(),
            // "AICONVERSE" => new AiConverseChunk(),
            "GAM_FILE" => new GamFile(),
            "TXLIST" => new TxList(),
            "WREXT" => new WorldRep(),
            "BRLIST" => new BrList(),
            "RENDPARAMS" => new RendParams(),
            "P$ModelName" => new PropertyChunk<PropLabel>(),
            "P$Scale" => new PropertyChunk<PropVector>(),
            "P$RenderTyp" => new PropertyChunk<PropRenderType>(),
            "P$OTxtRepr0" => new PropertyChunk<PropString>(),
            "P$OTxtRepr1" => new PropertyChunk<PropString>(),
            "P$OTxtRepr2" => new PropertyChunk<PropString>(),
            "P$OTxtRepr3" => new PropertyChunk<PropString>(),
            "P$Light" => new PropertyChunk<PropLight>(),
            "P$AnimLight" => new PropertyChunk<PropAnimLight>(),
            "P$LightColo" => new PropertyChunk<PropLightColor>(),
            "P$Spotlight" => new PropertyChunk<PropSpotlight>(),
            "P$RenderAlp" => new PropertyChunk<PropFloat>(),
            "LD$MetaProp" => new LinkDataMetaProp(),
            _ when entryName.StartsWith("L$") => new LinkChunk(),
            _ when entryName.StartsWith("P$") => new PropertyChunk<PropGeneric>(),
            _ => new GenericChunk(),
        };
    }
}