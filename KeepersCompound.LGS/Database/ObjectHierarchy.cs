
using System;
using System.Collections.Generic;
using KeepersCompound.LGS.Database.Chunks;

namespace KeepersCompound.LGS.Database;

public class ObjectHierarchy
{
    public class DarkObject
    {
        public int objectId;
        public int parentId;
        public Dictionary<string, Property> properties;

        public DarkObject(int id)
        {
            objectId = id;
            parentId = 0;
            properties = new Dictionary<string, Property>();
        }

        public T GetProperty<T>(string propName) where T : Property
        {
            if (properties.TryGetValue(propName, out var prop))
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

        T GetMergedChunk<T>(string name) where T : IMergable
        {
            if (db.Chunks.TryGetValue(name, out var rawChunk))
            {
                var chunk = (T)rawChunk;
                if (gam != null && gam.Chunks.TryGetValue(name, out var rawGamChunk))
                {
                    var gamChunk = (T)rawGamChunk;
                    gamChunk.Merge(chunk);
                    return gamChunk;
                }
                return chunk;
            }

            throw new ArgumentException($"No chunk with name ({name}) found", nameof(name));
        }

        // Add parentages
        var metaPropLinks = GetMergedChunk<LinkChunk>("L$MetaProp");
        var metaPropLinkData = GetMergedChunk<LinkDataMetaProp>("LD$MetaProp");
        var length = metaPropLinks.links.Count;
        for (var i = 0; i < length; i++)
        {
            var link = metaPropLinks.links[i];
            var linkData = metaPropLinkData.linkData[i];
            var childId = link.source;
            var parentId = link.destination;
            if (!_objects.ContainsKey(childId))
            {
                _objects.Add(childId, new DarkObject(childId));
            }
            if (!_objects.ContainsKey(parentId))
            {
                _objects.Add(parentId, new DarkObject(parentId));
            }

            if (linkData.priority == 0)
            {
                _objects[childId].parentId = parentId;
            }
        }

        void AddProp<T>(string name) where T : Property, new()
        {
            var chunk = GetMergedChunk<PropertyChunk<T>>(name);
            foreach (var prop in chunk.properties)
            {
                var id = prop.objectId;
                if (!_objects.ContainsKey(id))
                {
                    _objects.Add(id, new DarkObject(id));
                }
                _objects[id].properties.TryAdd(name, prop);
            }
        }

        AddProp<PropLabel>("P$ModelName");
        AddProp<PropVector>("P$Scale");
        AddProp<PropRenderType>("P$RenderTyp");
        AddProp<PropString>("P$OTxtRepr0");
        AddProp<PropString>("P$OTxtRepr1");
        AddProp<PropString>("P$OTxtRepr2");
        AddProp<PropString>("P$OTxtRepr3");
        AddProp<PropFloat>("P$RenderAlp");
        AddProp<PropLight>("P$Light");
        AddProp<PropLightColor>("P$LightColo");
    }

    public T GetProperty<T>(int objectId, string propName) where T : Property
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
            if (prop != null)
            {
                return prop;
            }
            parentId = obj.parentId;
        }

        return null;
    }
}