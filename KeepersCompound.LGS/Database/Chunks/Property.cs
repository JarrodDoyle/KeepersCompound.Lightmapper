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

    public virtual void Write(BinaryWriter writer)
    {
        writer.Write(objectId);
        writer.Write((uint)length);
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
        foreach (var prop in properties)
        {
            prop.Write(writer);
        }
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(data);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(value ? 1 : 0);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(value);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.WriteNullString(value, length);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(stringLength);
        writer.WriteNullString(value, stringLength);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(value);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.WriteVec3(value);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write((uint)mode);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write((uint)effect);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write((uint)type);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        var flags = 0u;
        if (Bounce) flags += 1;
        if (DestroyOnImpact) flags += 2;
        if (SlayOnImpact) flags += 4;
        if (NoCollisionSound) flags += 8;
        if (NoResult) flags += 16;
        if (FullCollisionSound) flags += 32;
        writer.Write(flags);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.WriteVec3(Location);
        writer.Write(new byte[4]);
        writer.WriteRotation(Rotation);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(Brightness);
        writer.WriteVec3(Offset);
        writer.Write(Radius);
        writer.Write(QuadLit);
        writer.Write(new byte[3]);
        writer.Write(InnerRadius);
    }
}

public class PropAnimLight : Property
{
    public enum AnimMode
    {
        FlipMinMax,
        SlideSmoothly,
        Random,
        MinBrightness,
        MaxBrightness,
        ZeroBrightness,
        SmoothlyBrighten,
        SmoothlyDim,
        RandomButCoherent,
        FlickerMinMax,
    }

    // Standard light props
    public float Brightness;
    public Vector3 Offset;
    public float Radius;
    public float InnerRadius;
    public bool QuadLit;
    public bool Dynamic;

    // Animation
    public AnimMode Mode;
    public int MsToBrighten;
    public int MsToDim;
    public float MinBrightness;
    public float MaxBrightness;

    // Start state
    public float CurrentBrightness;
    public bool Rising;
    public int Timer;
    public bool Inactive;

    // World rep info
    public bool Refresh; // Not relevant to us. It's used to tell dromed it needs to relight
    public ushort LightTableMapIndex;
    public ushort LightTableLightIndex;
    public ushort CellsReached;

    private int _unknown;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Brightness = reader.ReadSingle();
        Offset = reader.ReadVec3();
        Refresh = reader.ReadBoolean();
        reader.ReadBytes(3);
        LightTableMapIndex = reader.ReadUInt16();
        CellsReached = reader.ReadUInt16();
        LightTableLightIndex = reader.ReadUInt16();
        Mode = (AnimMode)reader.ReadUInt16();
        MsToBrighten = reader.ReadInt32();
        MsToDim = reader.ReadInt32();
        MinBrightness = reader.ReadSingle();
        MaxBrightness = reader.ReadSingle();
        CurrentBrightness = reader.ReadSingle();
        Rising = reader.ReadBoolean();
        reader.ReadBytes(3);
        Timer = reader.ReadInt32();
        Inactive = reader.ReadBoolean();
        reader.ReadBytes(3);
        Radius = reader.ReadSingle();
        _unknown = reader.ReadInt32();
        QuadLit = reader.ReadBoolean();
        reader.ReadBytes(3);
        InnerRadius = reader.ReadSingle();
        Dynamic = reader.ReadBoolean();
        reader.ReadBytes(3);
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(Brightness);
        writer.WriteVec3(Offset);
        writer.Write(Refresh);
        writer.Write(new byte[3]);
        writer.Write(LightTableMapIndex);
        writer.Write(CellsReached);
        writer.Write(LightTableLightIndex);
        writer.Write((ushort)Mode);
        writer.Write(MsToBrighten);
        writer.Write(MsToDim);
        writer.Write(MinBrightness);
        writer.Write(MaxBrightness);
        writer.Write(CurrentBrightness);
        writer.Write(Rising);
        writer.Write(new byte[3]);
        writer.Write(Timer);
        writer.Write(Inactive);
        writer.Write(new byte[3]);
        writer.Write(Radius);
        writer.Write(_unknown);
        writer.Write(QuadLit);
        writer.Write(new byte[3]);
        writer.Write(InnerRadius);
        writer.Write(Dynamic);
        writer.Write(new byte[3]);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(Hue);
        writer.Write(Saturation);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(InnerAngle);
        writer.Write(OuterAngle);
        writer.Write(new byte[4]);
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

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(InnerAngle);
        writer.Write(OuterAngle);
        writer.Write(SpotBrightness);
    }
}
