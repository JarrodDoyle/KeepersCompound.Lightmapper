using System.IO.Compression;
using Serilog;

namespace KeepersCompound.LGS;

// TODO: Make this nicer to use!
// Rather than navigating through the path manager Context should hold the current campaign resources
// Campaign resources should be lazy loaded (only get the paths when we first set it as campaign)
// Campaign resources should extend off of the base game resources and just overwrite any resource paths it needs to

internal enum ConfigFile
{
    Cam,
    CamExt,
    CamMod,
    Install,
    ConfigFileCount
}

public enum ResourceType
{
    Mission,
    Object,
    ObjectTexture,
    Texture
}

public class ResourcePathManager
{
    public record CampaignResources
    {
        public bool Initialised = false;
        public string Name;
        private readonly Dictionary<string, string> _missionPathMap = [];
        private readonly Dictionary<string, string> _texturePathMap = [];
        private readonly Dictionary<string, string> _objectPathMap = [];
        private readonly Dictionary<string, string> _objectTexturePathMap = [];

        public List<string> GetResourceNames(ResourceType type)
        {
            List<string> keys = type switch
            {
                ResourceType.Mission => [.. _missionPathMap.Keys],
                ResourceType.Object => [.. _objectPathMap.Keys],
                ResourceType.ObjectTexture => [.. _objectTexturePathMap.Keys],
                ResourceType.Texture => [.. _texturePathMap.Keys],
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
            keys.Sort();
            return keys;
        }

        public string GetResourcePath(ResourceType type, string name)
        {
            var map = type switch
            {
                ResourceType.Mission => _missionPathMap,
                ResourceType.Object => _objectPathMap,
                ResourceType.ObjectTexture => _objectTexturePathMap,
                ResourceType.Texture => _texturePathMap,
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
            return map.TryGetValue(name.ToLower(), out var resourcePath) ? resourcePath : null;
        }

        public void Initialise(string[] resPaths)
        {
            foreach (var dir in resPaths)
            {
                foreach (var path in Directory.GetFiles(dir))
                {
                    var convertedPath = ConvertSeparator(path);
                    var ext = Path.GetExtension(convertedPath).ToLower();
                    if (ext == ".mis" || ext == ".cow")
                    {
                        var baseName = Path.GetFileName(convertedPath).ToLower();
                        _missionPathMap[baseName] = convertedPath;
                    }
                }

                var texPaths = GetTexturePaths(dir);
                var objPaths = GetObjectPaths(dir);
                var objTexPaths = GetObjectTexturePaths(dir);
                Log.Information(
                    "Found {TexCount} textures, {ObjCount} objects, {ObjTexCount} object textures for campaign: {CampaignName}",
                    texPaths.Count, objPaths.Count, objTexPaths.Count, Name);

                foreach (var (resName, path) in texPaths)
                {
                    _texturePathMap[resName] = path;
                }

                foreach (var (resName, path) in objPaths)
                {
                    _objectPathMap[resName] = path;
                }

                foreach (var (resName, path) in objTexPaths)
                {
                    _objectTexturePathMap[resName] = path;
                }
            }

            Initialised = true;
        }

        public void Initialise(string[] resPaths, CampaignResources parent)
        {
            foreach (var (resName, path) in parent._texturePathMap)
            {
                _texturePathMap[resName] = path;
            }

            foreach (var (resName, path) in parent._objectPathMap)
            {
                _objectPathMap[resName] = path;
            }

            foreach (var (resName, path) in parent._objectTexturePathMap)
            {
                _objectTexturePathMap[resName] = path;
            }

            Initialise(resPaths);
        }
    }

    public bool Initialised { get; private set; }
    private readonly string _extractionPath;
    private string _fmsDir;
    private CampaignResources _omResources;
    private Dictionary<string, CampaignResources> _fmResources;

    public ResourcePathManager(string extractionPath)
    {
        _extractionPath = extractionPath;
    }

    public static string ConvertSeparator(string path)
    {
        return path.Replace('\\', '/');
    }

    public bool TryInit(string installPath)
    {
        // TODO:
        // - Determine if folder is a thief install
        // - Load all the (relevant) config files
        // - Get base paths from configs
        // - Build list of FM campaigns
        // - Initialise OM campaign resource paths
        // - Lazy load FM campaign resource paths (that inherit OM resources)

        Log.Information("Initialising path manager");

        if (!Directory.Exists(installPath))
        {
            return false;
        }

        // TODO: Should these paths be stored?
        if (!TryGetConfigPaths(installPath, out var configPaths))
        {
            return false;
        }

        // We need to know where all the texture and object resources are
        var installCfgLines = File.ReadAllLines(configPaths[(int)ConfigFile.Install]);
        var resnamePaths = GetValidInstallPaths(installPath, installCfgLines, "resname_base");
        if (resnamePaths.Count == 0)
        {
            Log.Error("No valid {Var} found", "resname_base");
            return false;
        }

        var zipPaths = new List<string>();
        foreach (var dir in resnamePaths)
        {
            foreach (var path in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(path).ToLower();
                if (name is "fam.crf" or "obj.crf")
                {
                    zipPaths.Add(path);
                }
            }
        }

        // Do the extraction bro
        // The path order is a priority order, so we don't want to overwrite any files when extracting
        // TODO: Check if there's any problems caused by case sensitivity
        for (var i = 0; i < zipPaths.Count; i++)
        {
            var zipPath = zipPaths[^(i + 1)];
            var resType = Path.GetFileNameWithoutExtension(zipPath);
            var extractPath = Path.Join(_extractionPath, resType);
            ZipFile.OpenRead(zipPath).ExtractToDirectory(extractPath, true);
        }

        var omPaths = GetValidInstallPaths(installPath, installCfgLines, "load_path");
        if (omPaths.Count == 0)
        {
            return false;
        }

        omPaths.Add(_extractionPath);
        _omResources = new CampaignResources();
        _omResources.Name = "";
        _omResources.Initialise([..omPaths]);

        var camModLines = File.ReadAllLines(configPaths[(int)ConfigFile.CamMod]);
        FindConfigVar(camModLines, "fm_path", out var fmsPath, "FMs");
        _fmsDir = Path.Join(installPath, fmsPath);
        Log.Information("Searching for FMS at: {FmsPath}", _fmsDir);

        // Build up the map of FM campaigns. These are uninitialised, we just want
        // to have their name
        _fmResources = new Dictionary<string, CampaignResources>();
        if (Directory.Exists(_fmsDir))
        {
            foreach (var dir in Directory.GetDirectories(_fmsDir))
            {
                var name = Path.GetFileName(dir);
                var fmResource = new CampaignResources();
                fmResource.Name = name;
                _fmResources.Add(name, fmResource);
            }
        }

        Initialised = true;
        return true;
    }

    public List<string> GetCampaignNames()
    {
        if (!Initialised)
        {
            return null;
        }

        var names = new List<string>(_fmResources.Keys);
        names.Sort();
        return names;
    }

    public CampaignResources GetCampaign(string campaignName)
    {
        if (campaignName == null || campaignName == "")
        {
            return _omResources;
        }

        if (!_fmResources.TryGetValue(campaignName, out var campaign))
        {
            Log.Error("Failed to find campaign: {CampaignName}", campaignName);
            throw new ArgumentException("No campaign found with given name", nameof(campaignName));
        }

        if (!campaign.Initialised)
        {
            var fmPath = Path.Join(_fmsDir, campaignName);
            campaign.Initialise([fmPath], _omResources);
        }

        return campaign;
    }

    private static Dictionary<string, string> GetObjectTexturePaths(string root)
    {
        string[] validExtensions = { ".dds", ".png", ".tga", ".pcx", ".gif", ".bmp", ".cel" };

        var dirOptions = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var texOptions = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true
        };

        var pathMap = new Dictionary<string, string>();
        foreach (var dir in Directory.EnumerateDirectories(root, "obj", dirOptions))
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*", texOptions))
            {
                var convertedPath = ConvertSeparator(path);
                var ext = Path.GetExtension(convertedPath);
                if (validExtensions.Contains(ext.ToLower()))
                {
                    var key = Path.GetFileNameWithoutExtension(convertedPath).ToLower();
                    pathMap.TryAdd(key, convertedPath);
                }
            }
        }

        return pathMap;
    }

    private static Dictionary<string, string> GetTexturePaths(string root)
    {
        string[] validExtensions = { ".dds", ".png", ".tga", ".pcx", ".gif", ".bmp", ".cel" };

        var famOptions = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var textureOptions = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true
        };

        var pathMap = new Dictionary<string, string>();
        foreach (var dir in Directory.EnumerateDirectories(root, "fam", famOptions))
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*", textureOptions))
            {
                var convertedPath = ConvertSeparator(path);
                var ext = Path.GetExtension(convertedPath);
                if (validExtensions.Contains(ext.ToLower()))
                {
                    var key = Path.GetRelativePath(root, convertedPath)[..^ext.Length].ToLower();
                    pathMap.TryAdd(key, convertedPath);
                }
            }
        }

        return pathMap;
    }

    private static Dictionary<string, string> GetObjectPaths(string root)
    {
        var dirOptions = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var binOptions = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true
        };

        var pathMap = new Dictionary<string, string>();
        foreach (var dir in Directory.EnumerateDirectories(root, "obj", dirOptions))
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*.bin", binOptions))
            {
                var convertedPath = ConvertSeparator(path);
                var key = Path.GetRelativePath(dir, convertedPath).ToLower();
                pathMap.TryAdd(key, convertedPath);
            }
        }

        return pathMap;
    }

    /// <summary>
    /// Get an array of all the Dark config file paths.
    /// </summary>
    /// <param name="installPath">Root directory of the Thief installation.</param>
    /// <param name="configPaths">Output array of config file paths</param>
    /// <returns><c>true</c> if all config files were found, <c>false</c> otherwise.</returns>
    private static bool TryGetConfigPaths(string installPath, out string[] configPaths)
    {
        configPaths = new string[(int)ConfigFile.ConfigFileCount];

        var searchOptions = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive
        };

        // `cam.cfg`, `cam_ext.cfg`, and `cam_mod.ini` are always in the root of the install.
        // The first two configs will tell us if any other configs are in non-default locations.
        // We can't just do a recursive search for everything else because they can potentially
        // be *outside* of the Thief installation.
        foreach (var path in Directory.GetFiles(installPath, "cam*", searchOptions))
        {
            var name = Path.GetFileName(path).ToLower();
            switch (name)
            {
                case "cam.cfg":
                    configPaths[(int)ConfigFile.Cam] = path;
                    break;
                case "cam_ext.cfg":
                    configPaths[(int)ConfigFile.CamExt] = path;
                    break;
                case "cam_mod.ini":
                    configPaths[(int)ConfigFile.CamMod] = path;
                    break;
            }
        }

        // TODO: Verify we found cam/cam_ext/cam_mod
        var camExtLines = File.ReadAllLines(configPaths[(int)ConfigFile.CamExt]);
        var camLines = File.ReadAllLines(configPaths[(int)ConfigFile.Cam]);

        bool FindCamVar(string varName, out string value, string defaultValue = "")
        {
            return FindConfigVar(camExtLines, varName, out value, defaultValue) ||
                   FindConfigVar(camLines, varName, out value, defaultValue);
        }

        FindCamVar("include_path", out var includePath, "./");

        if (!FindCamVar("game", out var gameName))
        {
            Log.Error("`game` not specified in Cam/CamExt");
            return false;
        }

        if (!FindCamVar($"{gameName}_include_install_cfg", out var installCfgName))
        {
            Log.Error("Install config file path not specified in Cam/CamExt");
            return false;
        }

        if (!FindCamVar("include_user_cfg", out var userCfgName))
        {
            Log.Error("User config file path not specified in Cam/CamExt");
            return false;
        }

        // TODO: How to handle case-insensitive absolute paths?
        // Fixup the include path to "work" cross-platform
        includePath = ConvertSeparator(includePath);
        includePath = Path.Join(installPath, includePath);
        if (!Directory.Exists(includePath))
        {
            Log.Error("Include path specified in Cam/CamExt does not exist: {IncludePath}", includePath);
            return false;
        }

        foreach (var path in Directory.GetFiles(includePath, "*.cfg", searchOptions))
        {
            var name = Path.GetFileName(path).ToLower();
            if (name == installCfgName.ToLower())
            {
                configPaths[(int)ConfigFile.Install] = path;
            }
        }

        // Check we found everything
        var found = 0;
        for (var i = 0; i < (int)ConfigFile.ConfigFileCount; i++)
        {
            var path = configPaths[i];
            if (path == null || path == "")
            {
                Log.Error("Failed to find {ConfigFile} config file", (ConfigFile)i);
                continue;
            }

            found++;
        }

        if (found != (int)ConfigFile.ConfigFileCount)
        {
            Log.Error("Failed to find all required config files in Thief installation directory");
            return false;
        }

        return true;
    }

    private static bool FindConfigVar(string[] lines, string varName, out string value, string defaultValue = "")
    {
        value = defaultValue;

        foreach (var line in lines)
        {
            if (line.StartsWith(varName))
            {
                value = line[(line.IndexOf(' ') + 1)..];
                return true;
            }
        }

        return false;
    }

    private static List<string> GetValidInstallPaths(string rootPath, string[] lines, string varName)
    {
        var validPaths = new List<string>();

        if (!FindConfigVar(lines, varName, out var paths))
        {
            Log.Error("Failed to find {VarName} in install config", varName);
            return validPaths;
        }

        foreach (var path in paths.Split('+'))
        {
            var dir = Path.Join(rootPath, ConvertSeparator(path));
            if (!Directory.Exists(dir))
            {
                Log.Warning("Install config references non-existent {VarName}: {Path}", varName, dir);
                continue;
            }

            validPaths.Add(dir);
        }

        return validPaths;
    }
}