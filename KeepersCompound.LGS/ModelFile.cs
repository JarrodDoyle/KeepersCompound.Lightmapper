using System.Numerics;
using System.Text;

namespace KeepersCompound.LGS;

// TODO: Remove all the things that don't actually need to be stored
public class ModelFile
{
    public enum VhotId
    {
        LightPosition = 1,
        LightDirection = 8,
        Anchor = 2,
        Particle1 = 3,
        Particle2 = 4,
        Particle3 = 5,
        Particle4 = 6,
        Particle5 = 7
    }

    public enum JointType
    {
        None,
        Rotate,
        Slide,
    }

    [Flags]
    public enum PolygonType
    {
        None = 0x00,
        Solid = 0x01,
        Wireframe = 0x02,
        Textured = 0x03,
        VertexNormals = 0x18,
        Paletted = 0x20,
    }

    public ModelFile(string filename)
    {
        if (!File.Exists(filename))
        {
            return;
        }

        using MemoryStream stream = new(File.ReadAllBytes(filename));
        using BinaryReader reader = new(stream, Encoding.UTF8, false);

        if (stream.Length < 8)
        {
            return;
        }

        BinHeader = new BHeader(reader);
        if (BinHeader.Signature != "LGMD" || BinHeader.Version < 3)
        {
            return;
        }

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

        stream.Seek(Header.FaceNormalOffset, SeekOrigin.Begin);
        FaceNormals = new Vector3[(Header.PolygonOffset - Header.FaceNormalOffset) / 12];
        for (var i = 0; i < FaceNormals.Length; i++)
        {
            FaceNormals[i] = reader.ReadVec3();
        }

        stream.Seek(Header.PolygonOffset, SeekOrigin.Begin);
        Polygons = [];
        for (var i = 0; i < Header.PolygonCount; i++)
        {
            Polygons.Add(new Polygon(reader, BinHeader.Version));
            if (stream.Position >= Header.NodeOffset)
            {
                break;
            }
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

        stream.Seek(Header.ObjectOffset, SeekOrigin.Begin);
        Objects = new SubObject[Header.ObjectCount];
        for (var i = 0; i < Objects.Length; i++)
        {
            Objects[i] = new SubObject(reader);
        }

        Valid = true;
    }

    public bool Valid { get; private set; }
    public BHeader BinHeader { get; set; }
    public MHeader Header { get; set; }
    public Vector3[] Vertices { get; }
    public Vector2[] Uvs { get; }
    public Vector3[] FaceNormals { get; }
    public List<Polygon> Polygons { get; }
    public Material[] Materials { get; }
    public VHot[] VHots { get; }
    public SubObject[] Objects { get; }

    // TODO: Apply transforms to normals and stuff
    public void ApplyJoints(float[] joints)
    {
        // Build map of objects to their parent id
        var objCount = Objects.Length;
        var parentIds = new int[objCount];
        for (var i = 0; i < objCount; i++)
        {
            parentIds[i] = -1;
        }

        for (var i = 0; i < objCount; i++)
        {
            var subObj = Objects[i];
            var childIdx = subObj.Child;
            while (childIdx != -1)
            {
                parentIds[childIdx] = i;
                childIdx = Objects[childIdx].Next;
            }
        }

        // Calculate base transforms for every subobj (including joint)
        var subObjTransforms = new Matrix4x4[objCount];
        for (var i = 0; i < objCount; i++)
        {
            var subObj = Objects[i];
            var objTrans = Matrix4x4.Identity;
            if (subObj.JointIdx != -1)
            {
                var ang = subObj.JointIdx >= joints.Length ? 0 : float.DegreesToRadians(joints[subObj.JointIdx]);
                // TODO: Is this correct? Should I use a manual rotation matrix?
                var jointRot = Matrix4x4.CreateFromYawPitchRoll(0, ang, 0);
                objTrans = jointRot * subObj.Transform;
            }

            subObjTransforms[i] = objTrans;
        }

        // Apply sub object transforms
        for (var i = 0; i < objCount; i++)
        {
            var subObj = Objects[i];
            var transform = subObjTransforms[i];

            // Build compound transformation
            var parentId = parentIds[i];
            while (parentId != -1)
            {
                transform *= subObjTransforms[parentId];
                parentId = parentIds[parentId];
            }

            for (var j = 0; j < subObj.VhotCount; j++)
            {
                var v = VHots[subObj.VhotStartIdx + j];
                v.Position = Vector3.Transform(v.Position, transform);
                VHots[subObj.VhotStartIdx + j] = v;
            }

            for (var j = 0; j < subObj.VertexCount; j++)
            {
                var v = Vertices[subObj.VertexStartIdx + j];
                Vertices[subObj.VertexStartIdx + j] = Vector3.Transform(v, transform);
            }
        }
    }

    public bool TryGetVhot(VhotId id, out VHot vhot)
    {
        foreach (var v in VHots)
        {
            if (v.Id == id)
            {
                vhot = v;
                return true;
            }
        }

        vhot = new VHot();
        return false;
    }

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
        public uint VertexNormalOffset { get; }
        public uint FaceNormalOffset { get; }
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
            VertexNormalOffset = reader.ReadUInt32();
            FaceNormalOffset = reader.ReadUInt32();
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

    public struct SubObject
    {
        public string Name;
        public JointType JointType;
        public int JointIdx;
        public float MinJointValue;
        public float MaxJointValue;
        public Matrix4x4 Transform;
        public short Child;
        public short Next;
        public ushort VhotStartIdx;
        public ushort VhotCount;
        public ushort VertexStartIdx;
        public ushort VertexCount;
        public ushort VertexNormalStartIdx;
        public ushort VertexNormalCount;
        public ushort FaceNormalStartIdx;
        public ushort FaceNormalCount;
        public ushort NodeStartIdx;
        public ushort NodeCount;

        public SubObject(BinaryReader reader)
        {
            Name = reader.ReadNullString(8);
            JointType = (JointType)reader.ReadByte();
            JointIdx = reader.ReadInt32();
            MinJointValue = reader.ReadSingle();
            MaxJointValue = reader.ReadSingle();
            var v1 = reader.ReadVec3();
            var v2 = reader.ReadVec3();
            var v3 = reader.ReadVec3();
            var v4 = reader.ReadVec3();
            Transform = new Matrix4x4(v1.X, v1.Y, v1.Z, 0, v2.X, v2.Y, v2.Z, 0, v3.X, v3.Y, v3.Z, 0, v4.X, v4.Y, v4.Z,
                1);
            Child = reader.ReadInt16();
            Next = reader.ReadInt16();
            VhotStartIdx = reader.ReadUInt16();
            VhotCount = reader.ReadUInt16();
            VertexStartIdx = reader.ReadUInt16();
            VertexCount = reader.ReadUInt16();
            VertexNormalStartIdx = reader.ReadUInt16();
            VertexNormalCount = reader.ReadUInt16();
            FaceNormalStartIdx = reader.ReadUInt16();
            FaceNormalCount = reader.ReadUInt16();
            NodeStartIdx = reader.ReadUInt16();
            NodeCount = reader.ReadUInt16();
        }
    }

    public struct Polygon
    {
        public ushort Index;
        public ushort Data;
        public PolygonType Type;
        public byte VertexCount;
        public ushort Normal;
        public float D;
        public ushort[] VertexIndices;
        public ushort[] VertexNormalIndices;
        public ushort[] UvIndices;
        public byte Material;

        public Polygon(BinaryReader reader, int version)
        {
            Index = reader.ReadUInt16();
            Data = reader.ReadUInt16();
            Type = (PolygonType)reader.ReadByte();
            VertexCount = reader.ReadByte();
            Normal = reader.ReadUInt16();
            D = reader.ReadSingle();
            VertexIndices = new ushort[VertexCount];
            for (var i = 0; i < VertexCount; i++)
            {
                VertexIndices[i] = reader.ReadUInt16();
            }

            VertexNormalIndices = new ushort[VertexCount];
            for (var i = 0; i < VertexCount; i++)
            {
                VertexNormalIndices[i] = reader.ReadUInt16();
            }

            UvIndices = new ushort[(Type & PolygonType.Textured) != 0 ? VertexCount : 0];
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

    public struct VHot
    {
        public VhotId Id;
        public Vector3 Position;

        public VHot(BinaryReader reader)
        {
            Id = (VhotId)reader.ReadInt32();
            Position = reader.ReadVec3();
        }
    }
}