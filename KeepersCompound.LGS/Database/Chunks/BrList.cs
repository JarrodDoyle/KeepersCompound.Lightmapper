using System.Numerics;

namespace KeepersCompound.LGS.Database.Chunks;

public enum Media
{
    Room = 0xFB,
    Flow = 0xFC,
    Object = 0xFD,
    Area = 0xFE,
    Light = 0xFF,
    FillSolid = 0x00,
    FillAir = 0x01,
    FillWater = 0x02,
    Flood = 0x03,
    Evaporate = 0x04,
    SolidToWater = 0x05,
    SolidToAir = 0x06,
    AirToSolid = 0x07,
    WaterToSolid = 0x08,
    Blockable = 0x09
};

public class BrList : IChunk
{
    // TODO: Add better handling of the different brush types
    public record Brush
    {
        public record TexInfo
        {
            public short Id;
            public ushort Rot;
            public short Scale;
            public ushort X;
            public ushort Y;

            public TexInfo(BinaryReader reader)
            {
                Id = reader.ReadInt16();
                Rot = reader.ReadUInt16();
                Scale = reader.ReadInt16();
                X = reader.ReadUInt16();
                Y = reader.ReadUInt16();
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(Id);
                writer.Write(Rot);
                writer.Write(Scale);
                writer.Write(X);
                writer.Write(Y);
            }
        };

        public short Id;
        public short Time;
        public uint BrushInfo;
        public short TextureId;
        public Media Media;
        public sbyte Flags;
        public Vector3 Position;
        public Vector3 Size;
        public Vector3 Angle;
        public short CurrentFaceIndex;
        public float GridLineSpacing;
        public Vector3 GridPhaseShift;
        public Vector3 GridOrientation;
        public bool GridEnabled;
        public byte NumFaces;
        public sbyte EdgeSelected;
        public sbyte PointSelected;
        public sbyte UseFlag;
        public sbyte GroupId;
        public TexInfo[] Txs;

        public Brush(BinaryReader reader)
        {
            Id = reader.ReadInt16();
            Time = reader.ReadInt16();
            BrushInfo = reader.ReadUInt32();
            TextureId = reader.ReadInt16();
            Media = (Media)reader.ReadByte();
            Flags = reader.ReadSByte();
            Position = reader.ReadVec3();
            Size = reader.ReadVec3();
            Angle = reader.ReadRotation();
            CurrentFaceIndex = reader.ReadInt16();
            GridLineSpacing = reader.ReadSingle();
            GridPhaseShift = reader.ReadVec3();
            GridOrientation = reader.ReadRotation();
            GridEnabled = reader.ReadBoolean();
            NumFaces = reader.ReadByte();
            EdgeSelected = reader.ReadSByte();
            PointSelected = reader.ReadSByte();
            UseFlag = reader.ReadSByte();
            GroupId = reader.ReadSByte();
            reader.ReadBytes(4);
            if ((sbyte)Media >= 0)
            {
                Txs = new TexInfo[NumFaces];
                for (var i = 0; i < NumFaces; i++)
                {
                    Txs[i] = new TexInfo(reader);
                }
            }
            else
            {
                Txs = Array.Empty<TexInfo>();
            }
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Id);
            writer.Write(Time);
            writer.Write(BrushInfo);
            writer.Write(TextureId);
            writer.Write((byte)Media);
            writer.Write(Flags);
            writer.WriteVec3(Position);
            writer.WriteVec3(Size);
            writer.WriteRotation(Angle);
            writer.Write(CurrentFaceIndex);
            writer.Write(GridLineSpacing);
            writer.WriteVec3(GridPhaseShift);
            writer.WriteRotation(GridOrientation);
            writer.Write(GridEnabled);
            writer.Write(NumFaces);
            writer.Write(EdgeSelected);
            writer.Write(PointSelected);
            writer.Write(UseFlag);
            writer.Write(GroupId);
            writer.Write(new byte[4]);
            foreach (var info in Txs)
            {
                info.Write(writer);
            }
        }
    }

    public ChunkHeader Header { get; set; }
    public List<Brush> Brushes { get; set; }

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        Brushes = new List<Brush>();
        while (reader.BaseStream.Position < entry.Offset + entry.Size + 24)
        {
            Brushes.Add(new Brush(reader));
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        foreach (var brush in Brushes)
        {
            brush.Write(writer);
        }
    }
}