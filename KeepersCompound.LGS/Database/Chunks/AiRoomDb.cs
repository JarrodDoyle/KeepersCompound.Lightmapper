using System.IO;

namespace KeepersCompound.LGS.Database.Chunks;

class AiRoomDb : IChunk
{
    public struct Cell
    {
        int Size { get; set; }
        uint[] CellIds { get; set; }

        public Cell(BinaryReader reader)
        {
            Size = reader.ReadInt32();
            CellIds = new uint[Size];
            for (var i = 0; i < Size; i++)
            {
                CellIds[i] = reader.ReadUInt32();
            }
        }

        public readonly void Write(BinaryWriter writer)
        {
            writer.Write(Size);
            for (var i = 0; i < Size; i++)
            {
                writer.Write(CellIds[i]);
            }
        }
    }

    public ChunkHeader Header { get; set; }
    public bool IsEmpty { get; set; }
    public int ValidCellCount { get; set; }
    public int CellCount { get; set; }
    public Cell[] Cells { get; set; }

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        IsEmpty = reader.ReadBoolean();
        reader.ReadBytes(3);
        ValidCellCount = reader.ReadInt32();
        CellCount = reader.ReadInt16();
        Cells = new Cell[CellCount];
        for (var i = 0; i < CellCount; i++)
        {
            Cells[i] = new Cell(reader);
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        writer.Write(IsEmpty);
        writer.Write(new byte[3]);
        writer.Write(ValidCellCount);
        writer.Write(CellCount);
        for (var i = 0; i < CellCount; i++)
        {
            Cells[i].Write(writer);
        }
        throw new System.NotImplementedException();
    }
}