using System.Numerics;
using System.Text;

namespace KeepersCompound.LGS;

public static class Extensions
{
    public static Vector3 ReadRotation(this BinaryReader reader)
    {
        var raw = new Vector3(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
        return raw * 360 / (ushort.MaxValue + 1);
    }

    public static void WriteRotation(this BinaryWriter writer, Vector3 rotation)
    {
        var raw = rotation * (ushort.MaxValue + 1) / 360;
        writer.Write((ushort)raw.X);
        writer.Write((ushort)raw.Y);
        writer.Write((ushort)raw.Z);
    }

    public static Vector3 ReadVec3(this BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    public static void WriteVec3(this BinaryWriter writer, Vector3 vec)
    {
        writer.Write(vec.X);
        writer.Write(vec.Y);
        writer.Write(vec.Z);
    }

    public static Vector2 ReadVec2(this BinaryReader reader)
    {
        return new Vector2(reader.ReadSingle(), reader.ReadSingle());
    }

    public static void WriteVec2(this BinaryWriter writer, Vector2 vec)
    {
        writer.Write(vec.X);
        writer.Write(vec.Y);
    }

    public static string ReadNullString(this BinaryReader reader, int length)
    {
        var tmpName = Encoding.UTF8.GetString(reader.ReadBytes(length));
        var idx = tmpName.IndexOf('\0');
        if (idx >= 0) tmpName = tmpName[..idx];
        return tmpName;
    }

    public static void WriteNullString(this BinaryWriter writer, string nullString, int length)
    {
        var writeBytes = new byte[length];
        var stringBytes = Encoding.UTF8.GetBytes(nullString);
        stringBytes[..Math.Min(length, stringBytes.Length)].CopyTo(writeBytes, 0);
        writer.Write(writeBytes);
    }
}