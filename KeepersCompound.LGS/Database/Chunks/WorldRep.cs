using System.Numerics;

namespace KeepersCompound.LGS.Database.Chunks;

public class WorldRep : IChunk
{
    public struct WrHeader
    {
        // Extended header content
        public int Size { get; set; }
        public int Version { get; set; }
        public int Flags { get; set; }
        public uint LightmapFormat { get; set; }
        public int LightmapScale { get; set; }

        // Standard header
        public uint DataSize { get; set; }
        public uint CellCount { get; set; }

        public WrHeader(BinaryReader reader)
        {
            Size = reader.ReadInt32();
            Version = reader.ReadInt32();
            Flags = reader.ReadInt32();
            LightmapFormat = reader.ReadUInt32();
            LightmapScale = reader.ReadInt32();
            DataSize = reader.ReadUInt32();
            CellCount = reader.ReadUInt32();
        }

        public readonly float LightmapScaleMultiplier()
        {
            return Math.Sign(LightmapScale) switch
            {
                1 => LightmapScale,
                -1 => 1.0f / LightmapScale,
                _ => 1.0f,
            };
        }

        public readonly void Write(BinaryWriter writer)
        {
            writer.Write(Size);
            writer.Write(Version);
            writer.Write(Flags);
            writer.Write(LightmapFormat);
            writer.Write(LightmapScale);
            writer.Write(DataSize);
            writer.Write(CellCount);
        }
    }

    public struct Cell
    {
        public struct Poly
        {
            public byte Flags { get; set; }
            public byte VertexCount { get; set; }
            public byte PlaneId { get; set; }
            public byte ClutId { get; set; }
            public ushort Destination { get; set; }
            public byte MotionIndex { get; set; }

            public Poly(BinaryReader reader)
            {
                Flags = reader.ReadByte();
                VertexCount = reader.ReadByte();
                PlaneId = reader.ReadByte();
                ClutId = reader.ReadByte();
                Destination = reader.ReadUInt16();
                MotionIndex = reader.ReadByte();
                reader.ReadByte();
            }

            public readonly void Write(BinaryWriter writer)
            {
                writer.Write(Flags);
                writer.Write(VertexCount);
                writer.Write(PlaneId);
                writer.Write(ClutId);
                writer.Write(Destination);
                writer.Write(MotionIndex);
                writer.Write((byte)0);
            }
        }

        public struct RenderPoly
        {
            public (Vector3, Vector3) TextureVectors { get; set; }
            public (float, float) TextureBases { get; set; }
            public ushort TextureId { get; set; }
            public ushort CachedSurface { get; set; }
            public float TextureMagnitude { get; set; }
            public Vector3 Center { get; set; }

            public RenderPoly(BinaryReader reader)
            {
                TextureVectors = (reader.ReadVec3(), reader.ReadVec3());
                TextureBases = (reader.ReadSingle(), reader.ReadSingle());
                TextureId = reader.ReadUInt16();
                CachedSurface = reader.ReadUInt16();
                TextureMagnitude = reader.ReadSingle();
                Center = reader.ReadVec3();
            }

            public readonly void Write(BinaryWriter writer)
            {
                writer.WriteVec3(TextureVectors.Item1);
                writer.WriteVec3(TextureVectors.Item2);
                writer.Write(TextureBases.Item1);
                writer.Write(TextureBases.Item2);
                writer.Write(TextureId);
                writer.Write(CachedSurface);
                writer.Write(TextureMagnitude);
                writer.WriteVec3(Center);
            }
        }

        public struct LightmapInfo
        {
            public (short, short) Bases { get; set; }
            public short PaddedWidth { get; set; }
            public byte Height { get; set; }
            public byte Width { get; set; }
            public uint DataPtr { get; set; }
            public uint DynamicLightPtr { get; set; }
            public uint AnimLightBitmask { get; set; }

            public LightmapInfo(BinaryReader reader)
            {
                Bases = (reader.ReadInt16(), reader.ReadInt16());
                PaddedWidth = reader.ReadInt16();
                Height = reader.ReadByte();
                Width = reader.ReadByte();
                DataPtr = reader.ReadUInt32();
                DynamicLightPtr = reader.ReadUInt32();
                AnimLightBitmask = reader.ReadUInt32();
            }

            public readonly void Write(BinaryWriter writer)
            {
                writer.Write(Bases.Item1);
                writer.Write(Bases.Item2);
                writer.Write(PaddedWidth);
                writer.Write(Height);
                writer.Write(Width);
                writer.Write(DataPtr);
                writer.Write(DynamicLightPtr);
                writer.Write(AnimLightBitmask);
            }
        }

        public struct Lightmap
        {
            public byte[] Pixels { get; set; }

            public int Layers;
            public int Width;
            public int Height;
            public int Bpp;

            public Lightmap(BinaryReader reader, byte width, byte height, uint bitmask, int bytesPerPixel)
            {
                var count = 1 + BitOperations.PopCount(bitmask);
                var length = bytesPerPixel * width * height * count;
                Pixels = reader.ReadBytes(length);
                Layers = count;
                Width = width;
                Height = height;
                Bpp = bytesPerPixel;
            }

            public readonly Vector4 GetPixel(uint layer, uint x, uint y)
            {
                if (layer >= Layers || x >= Width || y >= Height)
                {
                    return Vector4.Zero;
                }

                var idx = 0 + x * Bpp + y * Bpp * Width + layer * Bpp * Width * Height;
                switch (Bpp)
                {
                    case 1:
                        var raw1 = Pixels[idx];
                        return new Vector4(raw1, raw1, raw1, 255) / 255.0f;
                    case 2:
                        var raw2 = Pixels[idx] + (Pixels[idx + 1] << 8);
                        return new Vector4(raw2 & 31, (raw2 >> 5) & 31, (raw2 >> 10) & 31, 31) / 31.0f;
                    case 4:
                        return new Vector4(Pixels[idx + 2], Pixels[idx + 1], Pixels[idx], Pixels[idx + 3]) / 255.0f;
                    default:
                        return Vector4.Zero;
                }
            }

            public readonly byte[] AsBytesRgba(int layer)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(layer, 0, nameof(layer));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(layer, Layers, nameof(layer));

                var pIdx = layer * Bpp * Width * Height;
                var length = 4 * Width * Height;
                var bytes = new byte[length];
                for (var i = 0; i < length; i += 4, pIdx += Bpp)
                {
                    switch (Bpp)
                    {
                        case 1:
                            var raw1 = Pixels[pIdx];
                            bytes[i] = raw1;
                            bytes[i + 1] = raw1;
                            bytes[i + 2] = raw1;
                            bytes[i + 3] = 255;
                            break;
                        case 2:
                            var raw2 = Pixels[pIdx] + (Pixels[pIdx + 1] << 8);
                            bytes[i] = (byte)(255 * (raw2 & 31) / 31.0f);
                            bytes[i + 1] = (byte)(255 * ((raw2 >> 5) & 31) / 31.0f);
                            bytes[i + 2] = (byte)(255 * ((raw2 >> 10) & 31) / 31.0f);
                            bytes[i + 3] = 255;
                            break;
                        case 4:
                            bytes[i] = Pixels[pIdx + 2];
                            bytes[i + 1] = Pixels[pIdx + 1];
                            bytes[i + 2] = Pixels[pIdx];
                            bytes[i + 3] = Pixels[pIdx + 3];
                            break;
                    }
                }

                return bytes;
            }

            // TODO: This ONLY works for rgba (bpp = 4)!!!
            public readonly void AddLight(int layer, int x, int y, float r, float g, float b)
            {
                var idx = (x + y * Width + layer * Width * Height) * Bpp;
                Pixels[idx] = (byte)Math.Clamp(Pixels[idx] + r, 0, 255);
                Pixels[idx + 1] = (byte)Math.Clamp(Pixels[idx + 1] + g, 0, 255);
                Pixels[idx + 2] = (byte)Math.Clamp(Pixels[idx + 2] + b, 0, 255);
                Pixels[idx + 3] = 255;
            }

            public readonly void AddLight(int layer, int x, int y, Vector3 color, float strength, bool hdr)
            {
                if (hdr)
                {
                    strength /= 2.0f;
                }

                // We need to make sure we don't go over (255, 255, 255).
                // If we just do Max(color, (255, 255, 255)) then we change
                // the hue/saturation of coloured lights. Got to make sure we
                // maintain the colour ratios.
                var c = color * strength;
                var ratio = 0.0f;
                foreach (var e in new float[] { c.X, c.Y, c.Z })
                {
                    ratio = Math.Max(ratio, e / 255.0f);
                }

                if (ratio > 1.0f)
                {
                    c /= ratio;
                }

                AddLight(layer, x, y, c.Z, c.Y, c.X);
            }

            public readonly void Write(BinaryWriter writer)
            {
                writer.Write(Pixels);
            }
        }

        public byte VertexCount { get; set; }
        public byte PolyCount { get; set; }
        public byte RenderPolyCount { get; set; }
        public byte PortalPolyCount { get; set; }
        public byte PlaneCount { get; set; }
        public byte Medium { get; set; }
        public byte Flags { get; set; }
        public int PortalVertices { get; set; }
        public ushort NumVList { get; set; }
        public byte AnimLightCount { get; set; }
        public byte MotionIndex { get; set; }
        public Vector3 SphereCenter { get; set; }
        public float SphereRadius { get; set; }
        public Vector3[] Vertices { get; set; }
        public Poly[] Polys { get; set; }
        public RenderPoly[] RenderPolys { get; set; }
        public uint IndexCount { get; set; }
        public byte[] Indices { get; set; }
        public Plane[] Planes { get; set; }
        public ushort[] AnimLights { get; set; }
        public LightmapInfo[] LightList { get; set; }
        public Lightmap[] Lightmaps { get; set; }
        public int LightIndexCount { get; set; }
        public ushort[] LightIndices { get; set; }

        public Cell(BinaryReader reader, int bpp)
        {
            VertexCount = reader.ReadByte();
            PolyCount = reader.ReadByte();
            RenderPolyCount = reader.ReadByte();
            PortalPolyCount = reader.ReadByte();
            PlaneCount = reader.ReadByte();
            Medium = reader.ReadByte();
            Flags = reader.ReadByte();
            PortalVertices = reader.ReadInt32();
            NumVList = reader.ReadUInt16();
            AnimLightCount = reader.ReadByte();
            MotionIndex = reader.ReadByte();
            SphereCenter = reader.ReadVec3();
            SphereRadius = reader.ReadSingle();
            Vertices = new Vector3[VertexCount];
            for (var i = 0; i < VertexCount; i++)
            {
                Vertices[i] = reader.ReadVec3();
            }
            Polys = new Poly[PolyCount];
            for (var i = 0; i < PolyCount; i++)
            {
                Polys[i] = new Poly(reader);
            }
            RenderPolys = new RenderPoly[RenderPolyCount];
            for (var i = 0; i < RenderPolyCount; i++)
            {
                RenderPolys[i] = new RenderPoly(reader);
            }
            IndexCount = reader.ReadUInt32();
            Indices = new byte[IndexCount];
            for (var i = 0; i < IndexCount; i++)
            {
                Indices[i] = reader.ReadByte();
            }
            Planes = new Plane[PlaneCount];
            for (var i = 0; i < PlaneCount; i++)
            {
                Planes[i] = new Plane(reader.ReadVec3(), reader.ReadSingle());
            }
            AnimLights = new ushort[AnimLightCount];
            for (var i = 0; i < AnimLightCount; i++)
            {
                AnimLights[i] = reader.ReadUInt16();
            }
            LightList = new LightmapInfo[RenderPolyCount];
            for (var i = 0; i < RenderPolyCount; i++)
            {
                LightList[i] = new LightmapInfo(reader);
            }
            Lightmaps = new Lightmap[RenderPolyCount];
            for (var i = 0; i < RenderPolyCount; i++)
            {
                var info = LightList[i];
                Lightmaps[i] = new Lightmap(reader, info.Width, info.Height, info.AnimLightBitmask, bpp);
            }
            LightIndexCount = reader.ReadInt32();
            LightIndices = new ushort[LightIndexCount];
            for (var i = 0; i < LightIndexCount; i++)
            {
                LightIndices[i] = reader.ReadUInt16();
            }
        }

        public readonly void Write(BinaryWriter writer)
        {
            writer.Write(VertexCount);
            writer.Write(PolyCount);
            writer.Write(RenderPolyCount);
            writer.Write(PortalPolyCount);
            writer.Write(PlaneCount);
            writer.Write(Medium);
            writer.Write(Flags);
            writer.Write(PortalVertices);
            writer.Write(NumVList);
            writer.Write(AnimLightCount);
            writer.Write(MotionIndex);
            writer.WriteVec3(SphereCenter);
            writer.Write(SphereRadius);
            foreach (var vertex in Vertices)
            {
                writer.WriteVec3(vertex);
            }
            foreach (var poly in Polys)
            {
                poly.Write(writer);
            }
            foreach (var renderPoly in RenderPolys)
            {
                renderPoly.Write(writer);
            }
            writer.Write(IndexCount);
            writer.Write(Indices);
            foreach (var plane in Planes)
            {
                writer.WriteVec3(plane.Normal);
                writer.Write(plane.D);
            }
            foreach (var animLight in AnimLights)
            {
                writer.Write(animLight);
            }
            foreach (var lightmapInfo in LightList)
            {
                lightmapInfo.Write(writer);
            }
            foreach (var lightmap in Lightmaps)
            {
                lightmap.Write(writer);
            }
            writer.Write(LightIndexCount);
            foreach (var lightIndex in LightIndices)
            {
                writer.Write(lightIndex);
            }
        }
    }

    public struct BspTree
    {
        public struct Node
        {
            int parentIndex; // TODO: Split the flags out of this
            int cellId;
            int planeId;
            uint insideIndex;
            uint outsideIndex;

            public Node(BinaryReader reader)
            {
                parentIndex = reader.ReadInt32();
                cellId = reader.ReadInt32();
                planeId = reader.ReadInt32();
                insideIndex = reader.ReadUInt32();
                outsideIndex = reader.ReadUInt32();
            }

            public readonly void Write(BinaryWriter writer)
            {
                writer.Write(parentIndex);
                writer.Write(cellId);
                writer.Write(planeId);
                writer.Write(insideIndex);
                writer.Write(outsideIndex);
            }
        }

        public uint PlaneCount;
        public uint NodeCount;
        public Plane[] Planes;
        public Node[] Nodes;

        public BspTree(BinaryReader reader)
        {
            PlaneCount = reader.ReadUInt32();
            Planes = new Plane[PlaneCount];
            for (var i = 0; i < PlaneCount; i++)
            {
                Planes[i] = new Plane(reader.ReadVec3(), reader.ReadSingle());
            }

            NodeCount = reader.ReadUInt32();
            Nodes = new Node[NodeCount];
            for (var i = 0; i < NodeCount; i++)
            {
                Nodes[i] = new Node(reader);
            }
        }

        public readonly void Write(BinaryWriter writer)
        {
            writer.Write(PlaneCount);
            foreach (var plane in Planes)
            {
                writer.WriteVec3(plane.Normal);
                writer.Write(plane.D);
            }
            writer.Write(NodeCount);
            foreach (var node in Nodes)
            {
                node.Write(writer);
            }
        }
    }

    public struct LightTable
    {
        public struct LightData
        {
            public Vector3 Location;
            public Vector3 Direction;
            public Vector3 Color;
            float InnerAngle; // I'm pretty sure these are the spotlight angles
            float OuterAngle;
            float Radius;

            public LightData(BinaryReader reader)
            {
                Location = reader.ReadVec3();
                Direction = reader.ReadVec3();
                Color = reader.ReadVec3();
                InnerAngle = reader.ReadSingle();
                OuterAngle = reader.ReadSingle();
                Radius = reader.ReadSingle();
            }

            public readonly void Write(BinaryWriter writer)
            {
                writer.WriteVec3(Location);
                writer.WriteVec3(Direction);
                writer.WriteVec3(Color);
                writer.Write(InnerAngle);
                writer.Write(OuterAngle);
                writer.Write(Radius);
            }
        }

        public struct AnimCellMap
        {
            public ushort CellIndex;
            public ushort LightIndex;

            public AnimCellMap(BinaryReader reader)
            {
                CellIndex = reader.ReadUInt16();
                LightIndex = reader.ReadUInt16();
            }

            public readonly void Write(BinaryWriter writer)
            {
                writer.Write(CellIndex);
                writer.Write(LightIndex);
            }
        }

        public int LightCount;
        public int DynamicLightCount;
        public int AnimMapCount;
        public LightData[] Lights;
        public LightData[] ScratchpadLights;
        public AnimCellMap[] AnimCellMaps;

        // TODO: Support olddark
        public LightTable(BinaryReader reader)
        {
            LightCount = reader.ReadInt32();
            DynamicLightCount = reader.ReadInt32();
            Lights = new LightData[LightCount + DynamicLightCount];
            for (var i = 0; i < Lights.Length; i++)
            {
                Lights[i] = new LightData(reader);
            }
            ScratchpadLights = new LightData[32];
            for (var i = 0; i < ScratchpadLights.Length; i++)
            {
                ScratchpadLights[i] = new LightData(reader);
            }
            AnimMapCount = reader.ReadInt32();
            AnimCellMaps = new AnimCellMap[AnimMapCount];
            for (var i = 0; i < AnimCellMaps.Length; i++)
            {
                AnimCellMaps[i] = new AnimCellMap(reader);
            }
        }

        public readonly void Write(BinaryWriter writer)
        {
            writer.Write(LightCount);
            writer.Write(DynamicLightCount);
            foreach (var light in Lights)
            {
                light.Write(writer);
            }
            foreach (var light in ScratchpadLights)
            {
                light.Write(writer);
            }
            writer.Write(AnimMapCount);
            foreach (var map in AnimCellMaps)
            {
                map.Write(writer);
            }
        }
    }

    public ChunkHeader Header { get; set; }
    public WrHeader DataHeader { get; set; }
    public Cell[] Cells { get; set; }
    public BspTree Bsp { get; set; }
    public LightTable LightingTable { get; set; }
    private byte[] _unknown;
    private byte[] _unreadData;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        DataHeader = new(reader);
        var bpp = (DataHeader.LightmapFormat == 0) ? 2 : 4;

        Cells = new Cell[DataHeader.CellCount];
        for (var i = 0; i < DataHeader.CellCount; i++)
        {
            Cells[i] = new Cell(reader, bpp);
        }

        Bsp = new BspTree(reader);

        // TODO: Work out what this is
        _unknown = reader.ReadBytes(Cells.Length);
        LightingTable = new LightTable(reader);

        // TODO: All the other info lol
        var length = entry.Offset + entry.Size + 24 - reader.BaseStream.Position;
        _unreadData = reader.ReadBytes((int)length);
    }

    public void WriteData(BinaryWriter writer)
    {
        DataHeader.Write(writer);
        foreach (var cell in Cells)
        {
            cell.Write(writer);
        }
        Bsp.Write(writer);
        writer.Write(_unknown);
        LightingTable.Write(writer);
        writer.Write(_unreadData);
    }
}