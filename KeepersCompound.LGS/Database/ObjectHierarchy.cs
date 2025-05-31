using KeepersCompound.LGS.Database.Chunks;
using Serilog;

namespace KeepersCompound.LGS.Database;

public class ObjectHierarchy
{
    private class DarkObject
    {
        public int ObjectId;
        public int ParentId;
        public Dictionary<string, Property> Properties;

        public DarkObject(int id)
        {
            ObjectId = id;
            ParentId = 0;
            Properties = new Dictionary<string, Property>();
        }

        public T? GetProperty<T>(string propName) where T : Property
        {
            if (Properties.TryGetValue(propName, out var prop))
            {
                return (T)prop;
            }

            return null;
        }
    }

    private Dictionary<int, DarkObject> _objects;

    public ObjectHierarchy(DbFile db, DbFile gam = null)
    {
        _objects = new Dictionary<int, DarkObject>();

        bool TryGetMergedChunk<T>(string name, out T mergedChunk) where T : IMergeable
        {
            if (!db.TryGetChunk<T>(name, out var chunk))
            {
                mergedChunk = default;
                return false;
            }

            if (gam != null && gam.TryGetChunk<T>(name, out var gamChunk))
            {
                gamChunk.Merge(chunk);
                mergedChunk = gamChunk;
            }
            else
            {
                mergedChunk = chunk;
            }

            return true;

        }

        // Add parentages
        var metaPropExists = TryGetMergedChunk<LinkChunk>("L$MetaProp", out var metaPropLinks);
        var metaPropLinkDataExists = TryGetMergedChunk<LinkDataMetaProp>("LD$MetaProp", out var metaPropLinkData);
        if (!metaPropExists || !metaPropLinkDataExists)
        {
            Log.Error("Failed to get `MetaProp` chunks. Can't construct object hierarchy parentage.");
        }
        else
        {
            var length = metaPropLinks.Links.Count;
            for (var i = 0; i < length; i++)
            {
                var link = metaPropLinks.Links[i];
                var linkData = metaPropLinkData.LinkDatas[i];
                var childId = link.Source;
                var parentId = link.Destination;
                if (!_objects.ContainsKey(childId))
                {
                    _objects.Add(childId, new DarkObject(childId));
                }

                if (!_objects.ContainsKey(parentId))
                {
                    _objects.Add(parentId, new DarkObject(parentId));
                }

                if (linkData.Priority == 0)
                {
                    _objects[childId].ParentId = parentId;
                }
            }
        }

        void AddProp<T>(string name) where T : Property, new()
        {
            if (!TryGetMergedChunk<PropertyChunk<T>>(name, out var chunk))
            {
                Log.Warning("Can't add property {ChunkName} to hierarchy. Chunk doesn't exist.", name);
                return;
            }

            foreach (var prop in chunk.Properties)
            {
                var id = prop.ObjectId;
                if (!_objects.TryGetValue(id, out var value))
                {
                    value = new DarkObject(id);
                    _objects.Add(id, value);
                }

                value.Properties.TryAdd(name, prop);
            }
        }

        AddProp<PropLabel>("P$ModelName");
        AddProp<PropVector>("P$Scale");
        AddProp<PropRenderType>("P$RenderTyp");
        AddProp<PropJointPos>("P$JointPos");
        AddProp<PropBool>("P$Immobile");
        AddProp<PropBool>("P$StatShad");
        AddProp<PropString>("P$OTxtRepr0");
        AddProp<PropString>("P$OTxtRepr1");
        AddProp<PropString>("P$OTxtRepr2");
        AddProp<PropString>("P$OTxtRepr3");
        AddProp<PropFloat>("P$RenderAlp");
        AddProp<PropLight>("P$Light");
        AddProp<PropAnimLight>("P$AnimLight");
        AddProp<PropLightColor>("P$LightColo");
        AddProp<PropSpotlight>("P$Spotlight");
        AddProp<PropSpotlightAndAmbient>("P$SpotAmb");
    }

    // TODO: Work out if there's some nice way to automatically decide if we inherit
    public T? GetProperty<T>(int objectId, string propName, bool inherit = true) where T : Property
    {
        if (!_objects.ContainsKey(objectId))
        {
            return null;
        }

        var parentId = objectId;
        while (parentId != 0)
        {
            if (!_objects.TryGetValue(parentId, out var obj))
            {
                return null;
            }

            var prop = obj.GetProperty<T>(propName);
            if (prop != null || !inherit)
            {
                return prop;
            }

            parentId = obj.ParentId;
        }

        return null;
    }
}