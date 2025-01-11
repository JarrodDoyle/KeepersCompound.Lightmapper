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

    public class Cell
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

        public class LightmapInfo
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

            public void Write(BinaryWriter writer)
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

        public class Lightmap
        {
            private readonly bool[] _litLayers;
            public List<byte[]> Pixels { get; set; }

            public int Layers;
            public int Width;
            public int Height;
            public int Bpp;

            public Lightmap(BinaryReader reader, byte width, byte height, uint bitmask, int bytesPerPixel)
            {
                var layers = 1 + BitOperations.PopCount(bitmask);
                var length = bytesPerPixel * width * height;
                _litLayers = new bool[33];
                Pixels = new List<byte[]>();
                for (var i = 0; i < layers; i++)
                {
                    Pixels.Add(reader.ReadBytes(length));
                    _litLayers[i] = true;
                }
                Layers = layers;
                Width = width;
                Height = height;
                Bpp = bytesPerPixel;
            }

            public Vector4 GetPixel(uint layer, uint x, uint y)
            {
                if (layer >= Layers || x >= Width || y >= Height)
                {
                    return Vector4.Zero;
                }

                var pLayer = Pixels[(int)layer];
                var idx = x * Bpp + y * Bpp * Width;
                switch (Bpp)
                {
                    case 1:
                        var raw1 = pLayer[idx];
                        return new Vector4(raw1, raw1, raw1, 255) / 255.0f;
                    case 2:
                        var raw2 = pLayer[idx] + (pLayer[idx + 1] << 8);
                        return new Vector4(raw2 & 31, (raw2 >> 5) & 31, (raw2 >> 10) & 31, 31) / 31.0f;
                    case 4:
                        return new Vector4(pLayer[idx + 2], pLayer[idx + 1], pLayer[idx], pLayer[idx + 3]) / 255.0f;
                    default:
                        return Vector4.Zero;
                }
            }

            public byte[] AsBytesRgba(int layer)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(layer, 0, nameof(layer));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(layer, Layers, nameof(layer));

                var pLayer = Pixels[layer];
                var pIdx = 0;
                var length = 4 * Width * Height;
                var bytes = new byte[length];
                for (var i = 0; i < length; i += 4, pIdx += Bpp)
                {
                    switch (Bpp)
                    {
                        case 1:
                            var raw1 = pLayer[pIdx];
                            bytes[i] = raw1;
                            bytes[i + 1] = raw1;
                            bytes[i + 2] = raw1;
                            bytes[i + 3] = 255;
                            break;
                        case 2:
                            var raw2 = pLayer[pIdx] + (pLayer[pIdx + 1] << 8);
                            bytes[i] = (byte)(255 * (raw2 & 31) / 31.0f);
                            bytes[i + 1] = (byte)(255 * ((raw2 >> 5) & 31) / 31.0f);
                            bytes[i + 2] = (byte)(255 * ((raw2 >> 10) & 31) / 31.0f);
                            bytes[i + 3] = 255;
                            break;
                        case 4:
                            bytes[i] = pLayer[pIdx + 2];
                            bytes[i + 1] = pLayer[pIdx + 1];
                            bytes[i + 2] = pLayer[pIdx];
                            bytes[i + 3] = pLayer[pIdx + 3];
                            break;
                    }
                }

                return bytes;
            }

            public void AddLight(int layer, int x, int y, float r, float g, float b)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(layer, 0, nameof(layer));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(layer, Layers, nameof(layer));
                
                var idx = (x + y * Width) * Bpp;
                var pLayer = Pixels[layer];
                switch (Bpp)
                {
                    // 1bpp isn't really supported (does nd dromed even produce it?)
                    case 2:
                        var raw2 = pLayer[idx] + (pLayer[idx + 1] << 8);
                        var newR = (int)Math.Clamp((raw2 & 31) + r * 32f / 255f, 0, 31);
                        var newG = (int)Math.Clamp(((raw2 >> 5) & 31) + g * 32f / 255f, 0, 31);
                        var newB = (int)Math.Clamp(((raw2 >> 10) & 31) + b * 32f / 255f, 0, 31);
                        raw2 = newR + (newG << 5) + (newB << 10);
                        pLayer[idx] = (byte)(raw2 & 0xff);
                        pLayer[idx + 1] = (byte)((raw2 >> 8) & 0xff);
                        break;
                    case 4:
                        pLayer[idx] = (byte)Math.Clamp(pLayer[idx] + b, 0, 255);
                        pLayer[idx + 1] = (byte)Math.Clamp(pLayer[idx + 1] + g, 0, 255);
                        pLayer[idx + 2] = (byte)Math.Clamp(pLayer[idx + 2] + r, 0, 255);
                        pLayer[idx + 3] = 255;
                        break;
                }
                
                _litLayers[layer] = true;
            }

            public void AddLight(int layer, int x, int y, Vector3 color, float strength, bool hdr)
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
                var ratio = Math.Max(Math.Max(Math.Max(0.0f, c.X), c.Y), c.Z) / 255.0f;
                if (ratio > 1.0f)
                {
                    c /= ratio;
                }

                AddLight(layer, x, y, c.X, c.Y, c.Z);
            }

            public void Reset(Vector3 ambientLight, bool hdr)
            {
                Layers = 33;
                Pixels.Clear();
                var bytesPerLayer = Width * Height * Bpp;
                for (var i = 0; i < Layers; i++)
                {
                    Pixels.Add(new byte[bytesPerLayer]);
                    for (var j = 0; j < bytesPerLayer; j++)
                    {
                        Pixels[i][j] = 0;
                    }
                    _litLayers[i] = false;
                }

                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        AddLight(0, x, y, ambientLight, 1.0f, hdr);
                    }
                }
            }

            public void Write(BinaryWriter writer)
            {
                for (var i = 0; i < Layers; i++)
                {
                    if (_litLayers[i])
                    {
                        writer.Write(Pixels[i]);
                    }
                }
            }
        }

        public byte VertexCount { get; set; }
        public byte PolyCount { get; set; }
        public byte RenderPolyCount { get; set; }
        public byte PortalPolyCount { get; set; }
        public byte PlaneCount { get; set; }
        public byte Medium { get; set; }
        public byte Flags { get; set; } // TODO: Make these a [Flags] enum
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
        public List<ushort> AnimLights { get; set; }
        public LightmapInfo[] LightList { get; set; }
        public Lightmap[] Lightmaps { get; set; }
        public int LightIndexCount { get; set; }
        public List<ushort> LightIndices { get; set; }
        
        // Bonus data to make parallel iteration of cells easier
        public CellZone ZoneInfo { get; set; } = new();

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
            AnimLights = new List<ushort>(AnimLightCount);
            for (var i = 0; i < AnimLightCount; i++)
            {
                AnimLights.Add(reader.ReadUInt16());
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
            LightIndices = new List<ushort>(LightIndexCount);
            for (var i = 0; i < LightIndexCount; i++)
            {
                LightIndices.Add(reader.ReadUInt16());
            }
        }

        public void Write(BinaryWriter writer)
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

    // Readonly for now
    public record CellZone
    {
        private readonly byte _data;

        public CellZone()
        {
            _data = 0;
        }

        public CellZone(BinaryReader reader)
        {
            _data = reader.ReadByte();
        }

        public int GetAmbientLightZoneIndex()
        {
            return _data >> 4;
        }

        public int GetFogZoneIndex()
        {
            return _data & 0x0F;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(_data);
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

    public class LightTable
    {
        public struct LightData
        {
            public Vector3 Location;
            public Vector3 Direction;
            public Vector3 Color;
            public float InnerAngle; // I'm pretty sure these are the spotlight angles
            public float OuterAngle;
            public float Radius;

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
        public List<LightData> Lights;
        public LightData[] ScratchpadLights;
        public List<AnimCellMap> AnimCellMaps;

        // TODO: Support olddark
        public LightTable(BinaryReader reader)
        {
            LightCount = reader.ReadInt32();
            DynamicLightCount = reader.ReadInt32();
            var totalLightCount = LightCount + DynamicLightCount;
            Lights = new List<LightData>(totalLightCount);
            for (var i = 0; i < totalLightCount; i++)
            {
                Lights.Add(new LightData(reader));
            }
            ScratchpadLights = new LightData[32];
            for (var i = 0; i < 32; i++)
            {
                ScratchpadLights[i] = new LightData(reader);
            }
            AnimMapCount = reader.ReadInt32();
            AnimCellMaps = new List<AnimCellMap>(AnimMapCount);
            for (var i = 0; i < AnimMapCount; i++)
            {
                AnimCellMaps.Add(new AnimCellMap(reader));
            }
        }

        public void Write(BinaryWriter writer)
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

        public void Reset()
        {
            // Seems light we store something for sunlight at index 0 even if sunlight isn't being used
            // TODO: Work out what to actually *put* here
            LightCount = 1;
            Lights.Clear();
            Lights.Add(new LightData());

            DynamicLightCount = 0;
            AnimMapCount = 0;
            AnimCellMaps.Clear();
        }

        public void AddLight(LightData data, bool dynamicLight = false)
        {
            if (dynamicLight)
            {
                DynamicLightCount++;
                Lights.Add(data);
            }
            else
            {
                Lights.Insert(LightCount, data);
                LightCount++;
            }
        }
    }

    public ChunkHeader Header { get; set; }
    public WrHeader DataHeader { get; set; }
    public Cell[] Cells { get; set; }
    public BspTree Bsp { get; set; }
    public CellZone[] CellZones { get; set; }
    public LightTable LightingTable { get; set; }
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
        CellZones = new CellZone[Cells.Length];
        for (var i = 0; i < Cells.Length; i++)
        {
            var zone = new CellZone(reader);
            CellZones[i] = zone;
            Cells[i].ZoneInfo = zone;
        }
        
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
        foreach (var cellZone in CellZones)
        {
            cellZone.Write(writer);
        }
        LightingTable.Write(writer);
        writer.Write(_unreadData);
    }
}