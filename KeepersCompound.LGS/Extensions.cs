using System.IO;
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

    public static Vector3 ReadVec3(this BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    public static Vector2 ReadVec2(this BinaryReader reader)
    {
        return new Vector2(reader.ReadSingle(), reader.ReadSingle());
    }

    public static string ReadNullString(this BinaryReader reader, int length)
    {
        var tmpName = Encoding.UTF8.GetString(reader.ReadBytes(length));
        var idx = tmpName.IndexOf('\0');
        if (idx >= 0) tmpName = tmpName[..idx];
        return tmpName;
    }
}
