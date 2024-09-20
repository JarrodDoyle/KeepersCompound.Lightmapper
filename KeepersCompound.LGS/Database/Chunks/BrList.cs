using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace KeepersCompound.LGS.Database.Chunks;

public class BrList : IChunk
{
    // TODO: Add better handling of the different brush types
    public record Brush
    {
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
            Blockable = 0x09,
        };

        public record TexInfo
        {
            public short id;
            public ushort rot;
            public short scale;
            public ushort x;
            public ushort y;

            public TexInfo(BinaryReader reader)
            {
                id = reader.ReadInt16();
                rot = reader.ReadUInt16();
                scale = reader.ReadInt16();
                x = reader.ReadUInt16();
                y = reader.ReadUInt16();
            }
        };

        public short id;
        public short time;
        public uint brushInfo;
        public short textureId;
        public Media media;
        public sbyte flags;
        public Vector3 position;
        public Vector3 size;
        public Vector3 angle;
        public short currentFaceIndex;
        public float gridLineSpacing;
        public Vector3 gridPhaseShift;
        public Vector3 gridOrientation;
        public bool gridEnabled;
        public byte numFaces;
        public sbyte edgeSelected;
        public sbyte pointSelected;
        public sbyte useFlag;
        public sbyte groupId;
        public TexInfo[] txs;

        public Brush(BinaryReader reader)
        {
            id = reader.ReadInt16();
            time = reader.ReadInt16();
            brushInfo = reader.ReadUInt32();
            textureId = reader.ReadInt16();
            media = (Media)reader.ReadByte();
            flags = reader.ReadSByte();
            position = reader.ReadVec3();
            size = reader.ReadVec3();
            angle = reader.ReadRotation();
            currentFaceIndex = reader.ReadInt16();
            gridLineSpacing = reader.ReadSingle();
            gridPhaseShift = reader.ReadVec3();
            gridOrientation = reader.ReadRotation();
            gridEnabled = reader.ReadBoolean();
            numFaces = reader.ReadByte();
            edgeSelected = reader.ReadSByte();
            pointSelected = reader.ReadSByte();
            useFlag = reader.ReadSByte();
            groupId = reader.ReadSByte();
            reader.ReadBytes(4);
            if ((sbyte)media >= 0)
            {
                txs = new TexInfo[numFaces];
                for (var i = 0; i < numFaces; i++)
                {
                    txs[i] = new TexInfo(reader);
                }
            }
            else
            {
                txs = Array.Empty<TexInfo>();
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
        throw new System.NotImplementedException();
    }
}