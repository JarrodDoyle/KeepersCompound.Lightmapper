using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Serilog;

namespace KeepersCompound.LGS.Resources;

public class VirtualFileSystem
{
    private readonly Dictionary<string, BaseVirtualFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Files => _files.Keys.ToList();
    public int FileCount => _files.Count;
    public List<string> Folders => _folders.ToList();
    public int FolderCount => _folders.Count;

    public void Reset()
    {
        _files.Clear();
        _folders.Clear();
    }

    public bool Mount(string mountPoint, string path, bool recursive)
    {
        if (!Path.Exists(path))
        {
            Log.Warning("Cannot mount non-existent path: {Path}", path);
            return false;
        }

        if (Directory.Exists(path))
        {
            MountDirectory(mountPoint, path, recursive);
            return true;
        }

        if (!recursive)
        {
            return false;
        }

        try
        {
            MountZip(mountPoint, path);
            return true;
        }
        catch
        {
            Log.Warning("Failed to mount file: {Path}", path);
            return false;
        }
    }

    public bool Mount(string mountPoint, string path, HashSet<string> validExtensions, bool recursive)
    {
        if (!Path.Exists(path))
        {
            Log.Warning("Cannot mount non-existent path: {Path}", path);
            return false;
        }

        if (Directory.Exists(path))
        {
            MountDirectory(mountPoint, path, validExtensions, recursive);
        }
        else if (recursive)
        {
            try
            {
                MountZip(mountPoint, path, validExtensions);
            }
            catch
            {
                Log.Warning("Failed to mount file: {Path}", path);
                return false;
            }
        }
        else
        {
            return false;
        }

        return true;
    }

    public bool FileExists(string virtualPath)
    {
        return _files.ContainsKey(NormaliseFilePath(virtualPath));
    }

    public bool FolderExists(string virtualPath)
    {
        return _folders.Contains(NormaliseFolderPath(virtualPath));
    }

    public bool TryGetFileMemoryStream(string virtualPath, [MaybeNullWhen(false)] out MemoryStream stream)
    {
        if (_files.TryGetValue(NormaliseFilePath(virtualPath), out var file))
        {
            stream = file.GetMemoryStream();
            return true;
        }

        stream = null;
        return false;
    }

    public bool TryGetFilePath(string virtualPath, out string osPath)
    {
        if (_files.TryGetValue(NormaliseFilePath(virtualPath), out var file) && file is OsVirtualFile virtualFile)
        {
            osPath = virtualFile.OsPath;
            return true;
        }

        osPath = "";
        return false;
    }

    public HashSet<string> GetFilesInFolder(string folder, bool recursive)
    {
        folder = NormaliseFolderPath(folder);
        return _files.Keys.Where(path =>
        {
            if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return recursive || path.IndexOf('/', folder.Length) == -1;
        }).ToHashSet();
    }

    public HashSet<string> GetFilesInFolder(string folder, string[] extensions, bool recursive)
    {
        return GetFilesInFolder(folder, recursive).Where(path =>
        {
            var ext = Path.GetExtension(path);
            return extensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }).ToHashSet();
    }

    private void MountDirectory(string mountPoint, string path, bool recursive)
    {
        var searchOptions = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var osPath in Directory.GetFiles(path, "*", searchOptions))
        {
            var virtualPath = NormaliseFilePath(Path.GetRelativePath(path, osPath));
            var ext = Path.GetExtension(virtualPath).ToLower();
            if (ext == ".crf")
            {
                try
                {
                    MountZip(Path.Join(mountPoint, Path.GetDirectoryName(virtualPath) ?? ""), osPath);
                }
                catch
                {
                    Log.Warning("Failed to mount CRF: {Path}", path);
                }
            }
            else
            {
                RegisterParentDirectories(virtualPath);
                _files[virtualPath] = new OsVirtualFile(virtualPath, osPath);
            }
        }
    }

    private void MountDirectory(string mountPoint, string path, HashSet<string> validExtensions, bool recursive)
    {
        var searchOptions = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var osPath in Directory.GetFiles(path, "*", searchOptions))
        {
            var virtualPath = NormaliseFilePath(Path.GetRelativePath(path, osPath));
            var ext = Path.GetExtension(virtualPath).ToLower();
            if (ext == ".crf")
            {
                try
                {
                    MountZip(Path.Join(mountPoint, Path.GetDirectoryName(virtualPath) ?? ""), osPath);
                }
                catch
                {
                    Log.Warning("Failed to mount CRF: {Path}", path);
                }
            }
            else if (validExtensions.Contains(ext))
            {
                RegisterParentDirectories(virtualPath);
                _files[virtualPath] = new OsVirtualFile(virtualPath, osPath);
            }
        }
    }

    private void MountZip(string mountPoint, string path)
    {
        mountPoint = Path.Join(mountPoint, Path.GetFileNameWithoutExtension(path));

        var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            // There's no built-in way to check if an entry is a directory
            if (entry.FullName.Last() == '/')
                continue;

            var virtualPath = NormaliseFilePath(Path.Join(mountPoint, entry.FullName));
            RegisterParentDirectories(virtualPath);

            _files[virtualPath] = new ZipVirtualFile(virtualPath, archive, entry.FullName);
        }
    }

    private void MountZip(string mountPoint, string path, HashSet<string> validExtensions)
    {
        mountPoint = Path.Join(mountPoint, Path.GetFileNameWithoutExtension(path));

        var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            // There's no built-in way to check if an entry is a directory
            if (entry.FullName.Last() == '/')
                continue;

            var ext = Path.GetExtension(entry.FullName).ToLower();
            if (!validExtensions.Contains(ext))
            {
                continue;
            }

            var virtualPath = NormaliseFilePath(Path.Join(mountPoint, entry.FullName));
            RegisterParentDirectories(virtualPath);

            _files[virtualPath] = new ZipVirtualFile(virtualPath, archive, entry.FullName);
        }
    }

    private static string NormaliseFilePath(string path)
    {
        path = PathUtils.ConvertSeparator(path);
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path;
    }

    private static string NormaliseFolderPath(string path)
    {
        path = PathUtils.ConvertSeparator(path);
        if (!path.EndsWith('/'))
        {
            path += "/";
        }

        return NormaliseFilePath(path);
    }

    private void RegisterParentDirectories(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var parent = Directory.GetParent(path);
        while (parent != null)
        {
            _folders.Add(NormaliseFolderPath(parent.FullName));
            parent = parent.Parent;
        }
    }
}