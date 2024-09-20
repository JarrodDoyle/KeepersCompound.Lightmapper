using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace KeepersCompound.LGS.Database.Chunks;

public class Property
{
    public int objectId;
    public int length;

    public virtual void Read(BinaryReader reader)
    {
        objectId = reader.ReadInt32();
        length = (int)reader.ReadUInt32();
    }
}

public class PropertyChunk<T> : IChunk, IMergable where T : Property, new()
{
    public ChunkHeader Header { get; set; }
    public List<T> properties;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        properties = new List<T>();
        while (reader.BaseStream.Position < entry.Offset + entry.Size + 24)
        {
            var prop = new T();
            prop.Read(reader);
            properties.Add(prop);
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        throw new System.NotImplementedException();
    }

    public void Merge(IMergable other)
    {
        properties.AddRange(((PropertyChunk<T>)other).properties);
    }
}

public class PropGeneric : Property
{
    public byte[] data;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        data = reader.ReadBytes(length);
    }
}

public class PropBool : Property
{
    public bool value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        value = reader.ReadInt32() != 0;
    }
}

public class PropInt : Property
{
    public int value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        value = reader.ReadInt32();
    }
}

public class PropLabel : Property
{
    public string value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        value = reader.ReadNullString(length);
    }
}

public class PropString : Property
{
    public int stringLength;
    public string value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        stringLength = reader.ReadInt32();
        value = reader.ReadNullString(stringLength);
    }
}

public class PropFloat : Property
{
    public float value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        value = reader.ReadSingle();
    }
}

public class PropVector : Property
{
    public Vector3 value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        value = reader.ReadVec3();
    }
}

public class PropRenderType : Property
{
    public enum Mode
    {
        Normal,
        NotRendered,
        Unlit,
        EditorOnly,
        CoronaOnly,
    }

    public Mode mode;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        mode = (Mode)reader.ReadUInt32();
    }
}

public class PropSlayResult : Property
{
    public enum Effect
    {
        Normal, NoEffect, Terminate, Destroy,
    }

    public Effect effect;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        effect = (Effect)reader.ReadUInt32();
    }
}

public class PropInventoryType : Property
{
    public enum Slot
    {
        Junk, Item, Weapon,
    }

    public Slot type;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        type = (Slot)reader.ReadUInt32();
    }
}

public class PropCollisionType : Property
{
    public bool Bounce;
    public bool DestroyOnImpact;
    public bool SlayOnImpact;
    public bool NoCollisionSound;
    public bool NoResult;
    public bool FullCollisionSound;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        var flags = reader.ReadUInt32();
        Bounce = (flags & 0x1) != 0;
        DestroyOnImpact = (flags & (0x1 << 1)) != 0;
        SlayOnImpact = (flags & (0x1 << 2)) != 0;
        NoCollisionSound = (flags & (0x1 << 3)) != 0;
        NoResult = (flags & (0x1 << 4)) != 0;
        FullCollisionSound = (flags & (0x1 << 5)) != 0;
    }
}

public class PropPosition : Property
{
    public Vector3 Location;
    public Vector3 Rotation;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Location = reader.ReadVec3();
        reader.ReadBytes(4); // Runtime Cell/Hint in editor
        Rotation = reader.ReadRotation();
    }
}

public class PropLight : Property
{
    public float Brightness;
    public Vector3 Offset;
    public float Radius;
    public float InnerRadius;
    public bool QuadLit;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Brightness = reader.ReadSingle();
        Offset = reader.ReadVec3();
        Radius = reader.ReadSingle();
        QuadLit = reader.ReadBoolean();
        reader.ReadBytes(3);
        InnerRadius = reader.ReadSingle();
    }
}

public class PropLightColor : Property
{
    public float Hue;
    public float Saturation;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Hue = reader.ReadSingle();
        Saturation = reader.ReadSingle();
    }
}

public class PropSpotlight : Property
{
    public float InnerAngle;
    public float OuterAngle;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        InnerAngle = reader.ReadSingle();
        OuterAngle = reader.ReadSingle();
        reader.ReadBytes(4); // Z is unused
    }
}

public class PropSpotlightAndAmbient : Property
{
    public float InnerAngle;
    public float OuterAngle;
    public float SpotBrightness;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        InnerAngle = reader.ReadSingle();
        OuterAngle = reader.ReadSingle();
        SpotBrightness = reader.ReadSingle();
    }
}

// TODO: Work out what this property actually does
public class PropLightBasedAlpha : Property
{
    public bool Enabled;
    public Vector2 AlphaRange;
    public Vector2 LightRange;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Enabled = reader.ReadBoolean();
        reader.ReadBytes(3);
        AlphaRange = reader.ReadVec2();
        LightRange = new Vector2(reader.ReadInt32(), reader.ReadInt32());
    }
}
