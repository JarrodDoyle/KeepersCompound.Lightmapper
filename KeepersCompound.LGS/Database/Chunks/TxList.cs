using System;
using System.IO;
using System.Text;

namespace KeepersCompound.LGS.Database.Chunks;

public class TxList : IChunk
{
    public struct Item
    {
        public byte[] Tokens { get; set; }
        public string Name { get; set; }

        public Item(BinaryReader reader)
        {
            Tokens = reader.ReadBytes(4);
            Name = reader.ReadNullString(16);
        }
    }


    public ChunkHeader Header { get; set; }

    public int BlockSize { get; set; }
    public int ItemCount { get; set; }
    public int TokenCount { get; set; }
    public string[] Tokens { get; set; }
    public Item[] Items { get; set; }


    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        BlockSize = reader.ReadInt32();
        ItemCount = reader.ReadInt32();
        TokenCount = reader.ReadInt32();
        Tokens = new string[TokenCount];
        for (var i = 0; i < TokenCount; i++)
        {
            Tokens[i] = reader.ReadNullString(16);
        }
        Items = new Item[ItemCount];
        for (var i = 0; i < ItemCount; i++)
        {
            Items[i] = new Item(reader);
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        throw new System.NotImplementedException();
    }
}