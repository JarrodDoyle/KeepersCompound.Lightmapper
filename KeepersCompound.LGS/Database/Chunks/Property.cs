using System.Numerics;

namespace KeepersCompound.LGS.Database.Chunks;

public class Property
{
    public int ObjectId;
    public int Length;

    public virtual void Read(BinaryReader reader)
    {
        ObjectId = reader.ReadInt32();
        Length = (int)reader.ReadUInt32();
    }

    public virtual void Write(BinaryWriter writer)
    {
        writer.Write(ObjectId);
        writer.Write((uint)Length);
    }
}

public class PropertyChunk<T> : IChunk, IMergable where T : Property, new()
{
    public ChunkHeader Header { get; set; }
    public List<T> Properties;

    public void ReadData(BinaryReader reader, DbFile.TableOfContents.Entry entry)
    {
        Properties = new List<T>();
        while (reader.BaseStream.Position < entry.Offset + entry.Size + 24)
        {
            var prop = new T();
            prop.Read(reader);
            Properties.Add(prop);
        }
    }

    public void WriteData(BinaryWriter writer)
    {
        foreach (var prop in Properties)
        {
            prop.Write(writer);
        }
    }

    public void Merge(IMergable other)
    {
        Properties.AddRange(((PropertyChunk<T>)other).Properties);
    }
}

public class PropGeneric : Property
{
    public byte[] Data;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Data = reader.ReadBytes(Length);
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(Data);
    }
}

public class PropBool : Property
{
    public bool Value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Value = reader.ReadInt32() != 0;
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(Value ? 1 : 0);
    }
}

public class PropInt : Property
{
    public int Value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Value = reader.ReadInt32();
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(Value);
    }
}

public class PropLabel : Property
{
    public string Value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Value = reader.ReadNullString(Length);
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.WriteNullString(Value, Length);
    }
}

public class PropString : Property
{
    public int StringLength;
    public string Value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        StringLength = reader.ReadInt32();
        Value = reader.ReadNullString(StringLength);
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(StringLength);
        writer.WriteNullString(Value, StringLength);
    }
}

public class PropFloat : Property
{
    public float Value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Value = reader.ReadSingle();
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(Value);
    }
}

public class PropVector : Property
{
    public Vector3 Value;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Value = reader.ReadVec3();
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.WriteVec3(Value);
    }
}

public enum RenderMode
{
    Normal,
    NotRendered,
    Unlit,
    EditorOnly,
    CoronaOnly
}

public class PropRenderType : Property
{
    public RenderMode RenderMode;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        RenderMode = (RenderMode)reader.ReadUInt32();
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write((uint)RenderMode);
    }
}

public class PropSlayResult : Property
{
    public enum Effect
    {
        Normal,
        NoEffect,
        Terminate,
        Destroy
    }

    public Effect SlayEffect;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        SlayEffect = (Effect)reader.ReadUInt32();
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write((uint)SlayEffect);
    }
}

public class PropInventoryType : Property
{
    public enum Slot
    {
        Junk,
        Item,
        Weapon
    }

    public Slot Type;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Type = (Slot)reader.ReadUInt32();
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        writer.Write((uint)Type);
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
        if (Bounce)
        {
            flags += 1;
        }

        if (DestroyOnImpact)
        {
            flags += 2;
        }

        if (SlayOnImpact)
        {
            flags += 4;
        }

        if (NoCollisionSound)
        {
            flags += 8;
        }

        if (NoResult)
        {
            flags += 16;
        }

        if (FullCollisionSound)
        {
            flags += 32;
        }

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
        FlickerMinMax
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

public class PropJointPos : Property
{
    public float[] Positions;

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);
        Positions = new float[6];
        for (var i = 0; i < 6; i++)
        {
            Positions[i] = reader.ReadSingle();
        }
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);
        foreach (var position in Positions)
        {
            writer.Write(position);
        }
    }
}