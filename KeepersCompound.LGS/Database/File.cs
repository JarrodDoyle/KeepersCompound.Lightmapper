
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

    public readonly struct TableOfContents
    {
        public readonly struct Entry
        {
            public string Name { get; }
            public uint Offset { get; }
            public uint Size { get; }

            public Entry(BinaryReader reader)
            {
                Name = reader.ReadNullString(12);
                Offset = reader.ReadUInt32();
                Size = reader.ReadUInt32();
            }

            public override string ToString()
            {
                // return $"Name: {Name}, Offset: {O}"
                return base.ToString();
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
    }

    public FHeader Header { get; private set; }
    public TableOfContents Toc { get; }
    public Dictionary<string, IChunk> Chunks { get; set; }

    public DbFile(string filename)
    {
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
            "P$ModelName" => new PropertyChunk<PropLabel>(),
            "P$Scale" => new PropertyChunk<PropVector>(),
            "P$RenderTyp" => new PropertyChunk<PropRenderType>(),
            "P$OTxtRepr0" => new PropertyChunk<PropString>(),
            "P$OTxtRepr1" => new PropertyChunk<PropString>(),
            "P$OTxtRepr2" => new PropertyChunk<PropString>(),
            "P$OTxtRepr3" => new PropertyChunk<PropString>(),
            "P$RenderAlp" => new PropertyChunk<PropFloat>(),
            "LD$MetaProp" => new LinkDataMetaProp(),
            _ when entryName.StartsWith("L$") => new LinkChunk(),
            _ when entryName.StartsWith("P$") => new PropertyChunk<PropGeneric>(),
            _ => new GenericChunk(),
        };
    }
}