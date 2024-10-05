using System.Numerics;
using System.Text;

namespace KeepersCompound.LGS;

// TODO: Remove all the things that don't actually need to be stored
public class ModelFile
{
    public readonly struct BHeader
    {
        public string Signature { get; }
        public int Version { get; }

        public BHeader(BinaryReader reader)
        {
            Signature = reader.ReadNullString(4);
            Version = reader.ReadInt32();
        }
    }

    public readonly struct MHeader
    {
        public string Name { get; }
        public float Radius { get; }
        public float MaxPolygonRadius { get; }
        public Vector3 MaxBounds { get; }
        public Vector3 MinBounds { get; }
        public Vector3 Center { get; }

        public ushort PolygonCount { get; }
        public ushort VertexCount { get; }
        public ushort ParameterCount { get; }
        public byte MaterialCount { get; }
        public byte VCallCount { get; }
        public byte VHotCount { get; }
        public byte ObjectCount { get; }

        public uint ObjectOffset { get; }
        public uint MaterialOffset { get; }
        public uint UvOffset { get; }
        public uint VHotOffset { get; }
        public uint VertexOffset { get; }
        public uint LightOffset { get; }
        public uint NormalOffset { get; }
        public uint PolygonOffset { get; }
        public uint NodeOffset { get; }

        public uint ModelSize { get; }

        public uint AuxMaterialFlags { get; }
        public uint AuxMaterialOffset { get; }
        public uint AuxMaterialSize { get; }

        public MHeader(BinaryReader reader, int version)
        {
            Name = reader.ReadNullString(8);
            Radius = reader.ReadSingle();
            MaxPolygonRadius = reader.ReadSingle();
            MaxBounds = reader.ReadVec3();
            MinBounds = reader.ReadVec3();
            Center = reader.ReadVec3();
            PolygonCount = reader.ReadUInt16();
            VertexCount = reader.ReadUInt16();
            ParameterCount = reader.ReadUInt16();
            MaterialCount = reader.ReadByte();
            VCallCount = reader.ReadByte();
            VHotCount = reader.ReadByte();
            ObjectCount = reader.ReadByte();
            ObjectOffset = reader.ReadUInt32();
            MaterialOffset = reader.ReadUInt32();
            UvOffset = reader.ReadUInt32();
            VHotOffset = reader.ReadUInt32();
            VertexOffset = reader.ReadUInt32();
            LightOffset = reader.ReadUInt32();
            NormalOffset = reader.ReadUInt32();
            PolygonOffset = reader.ReadUInt32();
            NodeOffset = reader.ReadUInt32();
            ModelSize = reader.ReadUInt32();

            if (version == 4)
            {
                AuxMaterialFlags = reader.ReadUInt32();
                AuxMaterialOffset = reader.ReadUInt32();
                AuxMaterialSize = reader.ReadUInt32();
            }
            else
            {
                AuxMaterialFlags = 0;
                AuxMaterialOffset = 0;
                AuxMaterialSize = 0;
            }
        }
    }

    public struct Polygon
    {
        public ushort Index;
        public ushort Data;
        public byte Type;
        public byte VertexCount;
        public ushort Normal;
        public float D;
        public ushort[] VertexIndices;
        public ushort[] LightIndices;
        public ushort[] UvIndices;
        public byte Material;

        public Polygon(BinaryReader reader, int version)
        {
            Index = reader.ReadUInt16();
            Data = reader.ReadUInt16();
            Type = reader.ReadByte();
            VertexCount = reader.ReadByte();
            Normal = reader.ReadUInt16();
            D = reader.ReadSingle();
            VertexIndices = new ushort[VertexCount];
            for (var i = 0; i < VertexCount; i++)
            {
                VertexIndices[i] = reader.ReadUInt16();
            }
            LightIndices = new ushort[VertexCount];
            for (var i = 0; i < VertexCount; i++)
            {
                LightIndices[i] = reader.ReadUInt16();
            }
            UvIndices = new ushort[Type == 0x1B ? VertexCount : 0];
            for (var i = 0; i < UvIndices.Length; i++)
            {
                UvIndices[i] = reader.ReadUInt16();
            }

            Material = version == 4 ? reader.ReadByte() : (byte)0;
        }
    }

    public struct Material
    {
        public string Name;
        public byte Type;
        public byte Slot;
        public uint Handle;
        public float Uv;

        public Material(BinaryReader reader)
        {
            Name = reader.ReadNullString(16);
            Type = reader.ReadByte();
            Slot = reader.ReadByte();
            Handle = reader.ReadUInt32();
            Uv = reader.ReadSingle();
        }
    }

    public enum VhotId
    {
        LightPosition = 1,
        LightDirection = 8,
        Anchor = 2,
        Particle1 = 3,
        Particle2 = 4,
        Particle3 = 5,
        Particle4 = 6,
        Particle5 = 7,
    }

    public struct VHot
    {
        public int Id;
        public Vector3 Position;

        public VHot(BinaryReader reader)
        {
            Id = reader.ReadInt32();
            Position = reader.ReadVec3();
        }
    }

    public BHeader BinHeader { get; set; }
    public MHeader Header { get; set; }
    public Vector3[] Vertices { get; }
    public Vector2[] Uvs { get; }
    public Vector3[] Normals { get; }
    public Polygon[] Polygons { get; }
    public Material[] Materials { get; }
    public VHot[] VHots { get; }

    public ModelFile(string filename)
    {
        if (!File.Exists(filename)) return;

        using MemoryStream stream = new(File.ReadAllBytes(filename));
        using BinaryReader reader = new(stream, Encoding.UTF8, false);

        BinHeader = new BHeader(reader);
        if (BinHeader.Signature != "LGMD") return;

        Header = new MHeader(reader, BinHeader.Version);
        stream.Seek(Header.VertexOffset, SeekOrigin.Begin);
        Vertices = new Vector3[Header.VertexCount];
        for (var i = 0; i < Vertices.Length; i++)
        {
            Vertices[i] = reader.ReadVec3();
        }
        stream.Seek(Header.UvOffset, SeekOrigin.Begin);
        Uvs = new Vector2[(Header.VHotOffset - Header.UvOffset) / 8];
        for (var i = 0; i < Uvs.Length; i++)
        {
            Uvs[i] = reader.ReadVec2();
        }
        stream.Seek(Header.NormalOffset, SeekOrigin.Begin);
        Normals = new Vector3[(Header.PolygonOffset - Header.NormalOffset) / 12];
        for (var i = 0; i < Normals.Length; i++)
        {
            Normals[i] = reader.ReadVec3();
        }
        stream.Seek(Header.PolygonOffset, SeekOrigin.Begin);
        Polygons = new Polygon[Header.PolygonCount];
        for (var i = 0; i < Polygons.Length; i++)
        {
            Polygons[i] = new Polygon(reader, BinHeader.Version);
        }
        stream.Seek(Header.MaterialOffset, SeekOrigin.Begin);
        Materials = new Material[Header.MaterialCount];
        for (var i = 0; i < Materials.Length; i++)
        {
            Materials[i] = new Material(reader);
        }
        stream.Seek(Header.VHotOffset, SeekOrigin.Begin);
        VHots = new VHot[Header.VHotCount];
        for (var i = 0; i < VHots.Length; i++)
        {
            VHots[i] = new VHot(reader);
        }
    }

    public bool TryGetVhot(VhotId id, out VHot vhot)
    {
        foreach (var v in VHots)
        {
            if (v.Id == (int)id)
            {
                vhot = v;
                return true;
            }
        }
        vhot = new VHot();
        return false;
    }
}