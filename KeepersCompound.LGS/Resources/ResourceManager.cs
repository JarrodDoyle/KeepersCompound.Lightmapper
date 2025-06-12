using System.Diagnostics.CodeAnalysis;
using System.Text;
using KeepersCompound.LGS.Database;
using Serilog;

namespace KeepersCompound.LGS.Resources;

public class ResourceManager
{
    private static string[] _textureExtensions = [".dds", ".png", ".tga", ".pcx", ".gif", ".bmp", ".cel"];

    public HashSet<string> DbFileNames { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> TextureNames { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ObjectNames { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ObjectTextureNames { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    private VirtualFileSystem _vfs = new();
    private Dictionary<string, ModelFile> _modelCache = new(StringComparer.OrdinalIgnoreCase);

    public void Reset()
    {
        _vfs.Reset();
        _modelCache.Clear();
    }

    public void InitWithPaths(string[] paths)
    {
        Reset();

        // The path order is a priority order, so we load in reverse
        for (var i = 0; i < paths.Length; i++)
        {
            var path = paths[^(i + 1)];
            _vfs.Mount("", path);
        }
        DbFileNames = _vfs.GetFilesInFolder("", [".mis", ".cow", ".gam"], false);
        TextureNames = _vfs.GetFilesInFolder("fam", _textureExtensions, true);
        ObjectNames = _vfs.GetFilesInFolder("obj", [".bin"], false);
        ObjectTextureNames = _vfs.GetFilesInFolder("obj/txt", _textureExtensions, false);
        ObjectTextureNames.UnionWith(_vfs.GetFilesInFolder("obj/txt16", _textureExtensions, false));

        Log.Information(
            "Found {DbFiles} mis/gam/cow, {Textures} textures, {Objects} objects, {ObjectTextures} object textures",
            DbFileNames.Count, TextureNames.Count, ObjectNames.Count, ObjectTextureNames.Count);
    }

    public bool TryGetModel(string name, [MaybeNullWhen(false)] out ModelFile model)
    {
        if (_modelCache.TryGetValue(name, out model))
        {
            return true;
        }

        if (_vfs.TryGetFileMemoryStream(name, out var stream))
        {
            using BinaryReader reader = new(stream, Encoding.UTF8, false);
            model = new ModelFile(reader);
            _modelCache.Add(name, model);
            return true;
        }

        return false;
    }

    public bool TryGetDbFile(string name, [MaybeNullWhen(false)] out DbFile mission)
    {
        if (_vfs.TryGetFileMemoryStream(name, out var stream))
        {
            Log.Information("Loading DbFile: {VirtualPath}", name);
            using BinaryReader reader = new(stream, Encoding.UTF8, false);
            mission = new DbFile(reader);
            return true;
        }

        Log.Error("Failed to load DbFile. File does not exist.");
        mission = null;
        return false;
    }

    public bool TryGetFilePath(string virtualPath, out string osPath)
    {
        return _vfs.TryGetFilePath(virtualPath, out osPath);
    }

    public bool TryGetFileMemoryStream(string virtualPath, [MaybeNullWhen(false)] out MemoryStream memoryStream)
    {
        return _vfs.TryGetFileMemoryStream(virtualPath, out memoryStream);
    }

    public bool TryGetObjectTextureVirtualPath(string name, out string virtualPath)
    {
        foreach (var prefix in new []{"obj/txt16", "obj/txt"})
        {
            foreach (var ext in _textureExtensions)
            {
                virtualPath = $"{prefix}/{name}{ext}";
                if (_vfs.FileExists(virtualPath))
                {
                    return true;
                }
            }
        }

        virtualPath = "";
        return false;
    }
}