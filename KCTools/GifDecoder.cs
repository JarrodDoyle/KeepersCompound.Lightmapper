using System.Text;
using KeepersCompound.LGS;

namespace KCTools;

public class GifDecoder
{
    private readonly byte[] _globalColors;
    private readonly Header _header;
    private readonly ImageData[] _images;

    public GifDecoder(BinaryReader reader)
    {
        _header = new Header(reader);
        _globalColors = ReadColorTable(reader, _header.HasGlobalColorTable ? _header.GlobalColorTableSize : 0);
        var images = new List<ImageData>();
        while (true)
        {
            var id = reader.ReadByte();
            var eof = false;
            switch (id)
            {
                case 0x2C: // Image
                    images.Add(new ImageData(reader, _globalColors, 0));
                    break;
                case 0x21: // Extension
                    // We don't need to actually handle any extensions. The only one that's
                    // potentially relevant is GraphicsControl, but Dark uses a hardcoded
                    // transparency palette index and doesn't use multi-frame GIFs with timing.
                    reader.ReadByte();
                    var blockSize = reader.ReadByte();
                    while (blockSize != 0)
                    {
                        reader.ReadBytes(blockSize);
                        blockSize = reader.ReadByte();
                    }

                    break;
                case 0x3B:
                    eof = true;
                    break;
                default:
                    throw new InvalidDataException($"Unknown block identifier in GIF file: {id} at {reader.BaseStream.Position}");
            }

            if (eof)
            {
                break;
            }
        }

        _images = images.ToArray();
    }

    public ImageData GetImage(int idx)
    {
        return _images[idx];
    }

    private static byte[] ReadColorTable(BinaryReader reader, int length)
    {
        var colors = new byte[length * 3];
        for (var i = 0; i < length; i++)
        {
            colors[i * 3] = reader.ReadByte();
            colors[i * 3 + 1] = reader.ReadByte();
            colors[i * 3 + 2] = reader.ReadByte();
        }

        return colors;
    }

    private record Header
    {
        public readonly int GlobalColorTableSize;
        public readonly bool HasGlobalColorTable;
        public readonly string Signature;
        public byte BackgroundColorIndex;
        public byte BitsPerColorChannel; // Seemingly unused lol
        public bool IsGlobalColorTableSorted;
        public ushort LogicalScreenHeight;
        public ushort LogicalScreenWidth;
        public float PixelAspectRatio; // Ratio of width over height

        public Header(BinaryReader reader)
        {
            Signature = reader.ReadNullString(6);
            if (Signature != "GIF87a" && Signature != "GIF89a")
            {
                throw new InvalidDataException("File signature does not match GIF spec");
            }

            LogicalScreenWidth = reader.ReadUInt16();
            LogicalScreenHeight = reader.ReadUInt16();
            var flags = reader.ReadByte();
            HasGlobalColorTable = ((flags >> 7) & 0x1) != 0;
            BitsPerColorChannel = (byte)(((flags >> 4) & 0x7) + 1);
            IsGlobalColorTableSorted = ((flags >> 3) & 0x1) != 0;
            GlobalColorTableSize = (int)Math.Pow(2, (flags & 0x7) + 1);
            BackgroundColorIndex = reader.ReadByte();
            var rawAspectRatio = reader.ReadByte();
            if (rawAspectRatio == 0)
            {
                PixelAspectRatio = 1.0f;
            }
            else
            {
                PixelAspectRatio = (15 + rawAspectRatio) / 64.0f;
            }
        }
    }

    public record ImageData
    {
        private readonly byte[] _colors;
        private readonly bool _interlaced;
        private readonly byte[] _pixelIndices;
        private readonly int _transparentIndex;
        public int Height;
        public int Width;

        public ImageData(BinaryReader reader, byte[] globalColors, int transparentIndex)
        {
            var x = reader.ReadUInt16();
            var y = reader.ReadUInt16();
            var width = reader.ReadUInt16();
            var height = reader.ReadUInt16();
            var flags = reader.ReadByte();
            var hasLocalColorTable = ((flags >> 7) & 0x1) != 0;
            _interlaced = ((flags >> 6) & 0x1) != 0; // See appendix E for interlacing
            var isLocalColorTableSorted = ((flags >> 5) & 0x1) != 0;
            var sizeOfLocalColorTable = (byte)Math.Pow(2, (flags & 0x7) + 1);
            byte[] colors;
            if (hasLocalColorTable)
            {
                colors = ReadColorTable(reader, sizeOfLocalColorTable);
            }
            else
            {
                colors = globalColors;
            }

            // Now is the fun part. All the lovely LZW encoded pixel data :)
            var outIndex = 0;
            var pixelIndices = new byte[width * height];

            var minCodeSize = reader.ReadByte();
            var clearCode = 1 << minCodeSize;
            var endCode = 1 + (1 << minCodeSize);
            var table = new List<byte[]>();

            void ResetTable()
            {
                table.Clear();
                var len = 1 << minCodeSize;
                for (var i = 0; i < len; i++)
                {
                    table.Add([(byte)i]);
                }

                table.Add([]); // clear code
                table.Add([]); // end code
            }

            ResetTable();

            // Remember all this data is in blocks!!!
            var compressedBytes = new List<byte>();
            var blockSize = reader.ReadByte();
            while (blockSize != 0)
            {
                var bytes = reader.ReadBytes(blockSize);
                compressedBytes.AddRange(bytes);
                blockSize = reader.ReadByte();
            }

            using MemoryStream compressedStream = new(compressedBytes.ToArray());
            using BinaryReader compressedReader = new(compressedStream, Encoding.UTF8, false);

            var codeSize = minCodeSize + 1;
            var codeInputByte = compressedReader.ReadByte();
            var codeInputBit = 0;
            var previousCode = -1;
            while (true)
            {
                // Codes are variable length so we need to manage the bytes
                var code = 0;
                var codeBit = 0;
                while (codeBit < codeSize)
                {
                    var codeBitsLeft = codeSize - codeBit;
                    var inputBitsLeft = 8 - codeInputBit;
                    if (inputBitsLeft == 0)
                    {
                        codeInputByte = compressedReader.ReadByte();
                        codeInputBit = 0;
                        inputBitsLeft = 8;
                    }

                    var bitsToRead = Math.Min(inputBitsLeft, codeBitsLeft);
                    code += ((codeInputByte >> codeInputBit) & ((1 << bitsToRead) - 1)) << codeBit;
                    codeBit += bitsToRead;
                    codeInputBit += bitsToRead;
                }

                // Match the code
                var codeCount = table.Count;
                if (code == clearCode)
                {
                    ResetTable();
                    codeSize = minCodeSize + 1;
                    previousCode = -1;
                }
                else if (code == endCode)
                {
                    break;
                }
                else if (code < codeCount)
                {
                    // Write code W
                    var bytes = table[code];
                    foreach (var b in bytes)
                    {
                        pixelIndices[outIndex++] = b;
                    }

                    // Add new code = previous code + first "symbol" of W
                    if (previousCode != -1)
                    {
                        var previousBytes = table[previousCode];
                        var newCodeBytes = new byte[previousBytes.Length + 1];
                        var newCodeBytesIdx = 0;
                        foreach (var b in previousBytes)
                        {
                            newCodeBytes[newCodeBytesIdx++] = b;
                        }

                        newCodeBytes[newCodeBytesIdx] = bytes[0];
                        table.Add(newCodeBytes);
                    }

                    previousCode = code;
                }
                else if (code == codeCount)
                {
                    // Add new code = previous code + first symbol of previous code
                    var previousBytes = table[previousCode];
                    var bytes = new byte[previousBytes.Length + 1];
                    var bytesIdx = 0;
                    foreach (var b in previousBytes)
                    {
                        bytes[bytesIdx++] = b;
                    }

                    bytes[bytesIdx] = previousBytes[0];
                    table.Add(bytes);
                    // Write new code
                    foreach (var b in bytes)
                    {
                        pixelIndices[outIndex++] = b;
                    }

                    previousCode = code;
                }
                else
                {
                    throw new InvalidDataException("Code out of range");
                }

                // Increase codesize :)
                if (table.Count == 1 << codeSize)
                {
                    if (codeSize < 12)
                    {
                        codeSize += 1;
                    }
                    // else
                    // {
                    //     // pretty sure this is an error
                    //     throw new InvalidDataException("Code Size exceeding 12 bits");
                    // }
                }
            }

            Width = width;
            Height = height;
            _colors = colors;
            _pixelIndices = pixelIndices;
            _transparentIndex = transparentIndex;
        }

        public byte[] GetRgbaBytes()
        {
            var bytesIdx = 0;
            var bytes = new byte[Width * Height * 4];

            void WritePixelBytes(int bytesOffset, int colorIndex)
            {
                bytes[bytesOffset] = _colors[colorIndex * 3];
                bytes[bytesOffset + 1] = _colors[colorIndex * 3 + 1];
                bytes[bytesOffset + 2] = _colors[colorIndex * 3 + 2];
                bytes[bytesOffset + 3] = (byte)(colorIndex == _transparentIndex ? 0 : 255);
            }

            if (!_interlaced)
            {
                foreach (var colorIndex in _pixelIndices)
                {
                    WritePixelBytes(bytesIdx, colorIndex);
                    bytesIdx += 4;
                }

                return bytes;
            }

            var pass = 0;
            var inc = 8;
            var y = 0;
            var indicesOffset = 0;
            var bytesPerRow = Width * 4;
            for (var i = 0; i < Height; i++)
            {
                bytesIdx = y * bytesPerRow;
                for (var x = 0; x < Width; x++)
                {
                    WritePixelBytes(bytesIdx + x * 4, _pixelIndices[indicesOffset++]);
                }

                y += inc;
                if (y >= Height)
                {
                    pass += 1;
                    y = (int)(8 / Math.Pow(2, pass));
                    inc = y * 2;
                }
            }

            return bytes;
        }
    }
}