using System.IO.Compression;

namespace KeepersCompound.LGS;

// TODO: Make this nicer to use!
// Rather than navigating through the path manager Context should hold the current campaign resources
// Campaign resources should be lazy loaded (only get the paths when we first set it as campaign)
// Campaign resources should extend off of the base game resources and just overwrite any resource paths it needs to

enum ConfigFile
{
    Cam,
    CamExt,
    CamMod,
    Game,
    Install,
    User,
    ConfigFileCount,
}

public enum ResourceType
{
    Mission,
    Object,
    ObjectTexture,
    Texture,
}

public class ResourcePathManager
{
    public record CampaignResources
    {
        public bool initialised = false;
        public string name;
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
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
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
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
            return map.TryGetValue(name.ToLower(), out var resourcePath) ? resourcePath : null;
        }

        public void Initialise(string misPath, string resPath)
        {
            foreach (var path in Directory.GetFiles(misPath))
            {
                var convertedPath = ConvertSeparator(path);
                var ext = Path.GetExtension(convertedPath).ToLower();
                if (ext == ".mis" || ext == ".cow")
                {
                    var baseName = Path.GetFileName(convertedPath).ToLower();
                    _missionPathMap[baseName] = convertedPath;
                }
            }

            foreach (var (name, path) in GetTexturePaths(resPath))
            {
                _texturePathMap[name] = path;
            }
            foreach (var (name, path) in GetObjectPaths(resPath))
            {
                _objectPathMap[name] = path;
            }
            foreach (var (name, path) in GetObjectTexturePaths(resPath))
            {
                _objectTexturePathMap[name] = path;
            }
        }

        public void Initialise(string misPath, string resPath, CampaignResources parent)
        {
            foreach (var (name, path) in parent._texturePathMap)
            {
                _texturePathMap[name] = path;
            }
            foreach (var (name, path) in parent._objectPathMap)
            {
                _objectPathMap[name] = path;
            }
            foreach (var (name, path) in parent._objectTexturePathMap)
            {
                _objectTexturePathMap[name] = path;
            }

            Initialise(misPath, resPath);
        }
    }

    private bool _initialised = false;
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

    public void Init(string installPath)
    {
        // TODO:
        // - Determine if folder is a thief install
        // - Load all the (relevant) config files
        // - Get base paths from configs
        // - Build list of FM campaigns
        // - Initialise OM campaign resource paths
        // - Lazy load FM campaign resource paths (that inherit OM resources)

        if (!DirContainsThiefExe(installPath))
        {
            throw new ArgumentException($"No Thief installation found at {installPath}", nameof(installPath));
        }

        // TODO: Should these paths be stored?
        if (!TryGetConfigPaths(installPath, out var configPaths))
        {
            throw new InvalidOperationException("Failed to find all installation config paths.");
        }

        // Get the paths of the base Fam and Obj resources so we can extract them.
        var installCfgLines = File.ReadAllLines(configPaths[(int)ConfigFile.Install]);
        FindConfigVar(installCfgLines, "resname_base", out var resPaths);
        var baseFamPath = "";
        var baseObjPath = "";
        foreach (var resPath in resPaths.Split('+'))
        {
            var dir = Path.Join(installPath, ConvertSeparator(resPath));
            foreach (var path in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(path).ToLower();
                if (name == "fam.crf" && baseFamPath == "")
                {
                    baseFamPath = path;
                }
                else if (name == "obj.crf" && baseObjPath == "")
                {
                    baseObjPath = path;
                }
            }
        }

        // Do the extraction bro
        (string, string)[] resources = [("fam", baseFamPath), ("obj", baseObjPath)];
        foreach (var (extractName, zipPath) in resources)
        {
            var extractPath = Path.Join(_extractionPath, extractName);
            if (Directory.Exists(extractPath))
            {
                Directory.Delete(extractPath, true);
            }
            ZipFile.OpenRead(zipPath).ExtractToDirectory(extractPath);
        }

        FindConfigVar(installCfgLines, "load_path", out var omsPath);
        omsPath = Path.Join(installPath, ConvertSeparator(omsPath));
        _omResources = new CampaignResources();
        _omResources.name = "";
        _omResources.Initialise(omsPath, _extractionPath);

        var camModLines = File.ReadAllLines(configPaths[(int)ConfigFile.CamMod]);
        FindConfigVar(camModLines, "fm_path", out var fmsPath, "FMs");
        _fmsDir = Path.Join(installPath, fmsPath);

        // Build up the map of FM campaigns. These are uninitialised, we just want
        // to have their name
        _fmResources = new Dictionary<string, CampaignResources>();
        foreach (var dir in Directory.GetDirectories(_fmsDir))
        {
            var name = Path.GetFileName(dir);
            var fmResource = new CampaignResources();
            fmResource.name = name;
            _fmResources.Add(name, fmResource);
        }

        _initialised = true;
    }

    public List<string> GetCampaignNames()
    {
        if (!_initialised) return null;

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
        else if (_fmResources.TryGetValue(campaignName, out var campaign))
        {
            if (!campaign.initialised)
            {
                var fmPath = Path.Join(_fmsDir, campaignName);
                campaign.Initialise(fmPath, fmPath, _omResources);
            }
            return campaign;
        }

        throw new ArgumentException("No campaign found with given name", nameof(campaignName));
    }

    private static Dictionary<string, string> GetObjectTexturePaths(string root)
    {
        string[] validExtensions = { ".dds", ".png", ".tga", ".pcx", ".gif", ".bmp", ".cel", };

        var dirOptions = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var texOptions = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true,
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
        string[] validExtensions = { ".dds", ".png", ".tga", ".pcx", ".gif", ".bmp", ".cel", };

        var famOptions = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var textureOptions = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true,
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
            RecurseSubdirectories = true,
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
    /// Determine if the given directory contains a Thief executable at the top level.
    /// </summary>
    /// <param name="dir">The directory to search</param>
    /// <returns><c>true</c> if a Thief executable was found, <c>false</c> otherwise.</returns>
    private static bool DirContainsThiefExe(string dir)
    {
        var searchOptions = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        foreach (var path in Directory.GetFiles(dir, "*.exe", searchOptions))
        {
            var baseName = Path.GetFileName(path).ToLower();
            if (baseName.Contains("thief"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get an array of of all the Dark config file paths.
    /// </summary>
    /// <param name="installPath">Root directory of the Thief installation.</param>
    /// <param name="configPaths">Output array of config file paths</param>
    /// <returns><c>true</c> if all config files were found, <c>false</c> otherwise.</returns>
    private static bool TryGetConfigPaths(string installPath, out string[] configPaths)
    {
        configPaths = new string[(int)ConfigFile.ConfigFileCount];

        var searchOptions = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        // `cam.cfg`, `cam_ext.cfg`, and `cam_mod.ini` are always in the root of the install.
        // The first two configs will tell us if any other configs are in non-default locations.
        // We can't just do a recursive search for everything else because they can potentially
        // be *outside* of the Thief installation.
        foreach (var path in Directory.GetFiles(installPath, "cam*", searchOptions))
        {
            var name = Path.GetFileName(path).ToLower();
            if (name == "cam.cfg")
            {
                configPaths[(int)ConfigFile.Cam] = path;
            }
            else if (name == "cam_ext.cfg")
            {
                configPaths[(int)ConfigFile.CamExt] = path;
            }
            else if (name == "cam_mod.ini")
            {
                configPaths[(int)ConfigFile.CamMod] = path;
            }
        }

        var camExtLines = File.ReadAllLines(configPaths[(int)ConfigFile.CamExt]);
        var camLines = File.ReadAllLines(configPaths[(int)ConfigFile.Cam]);

        bool FindCamVar(string varName, out string value, string defaultValue = "")
        {
            return FindConfigVar(camExtLines, varName, out value, defaultValue) ||
                FindConfigVar(camLines, varName, out value, defaultValue);
        }

        FindCamVar("include_path", out var includePath, "./");
        FindCamVar("game", out var gameName);
        FindCamVar($"{gameName}_include_install_cfg", out var installCfgName);
        FindCamVar("include_user_cfg", out var userCfgName);

        // TODO: How to handle case-insensitive absolute paths?
        // Fixup the include path to "work" cross-platform
        includePath = ConvertSeparator(includePath);
        includePath = Path.Join(installPath, includePath);
        if (!Directory.Exists(includePath))
        {
            return false;
        }

        foreach (var path in Directory.GetFiles(includePath, "*.cfg", searchOptions))
        {
            var name = Path.GetFileName(path).ToLower();
            if (name == $"{gameName}.cfg")
            {
                configPaths[(int)ConfigFile.Game] = path;
            }
            else if (name == installCfgName.ToLower())
            {
                configPaths[(int)ConfigFile.Install] = path;
            }
            else if (name == userCfgName.ToLower())
            {
                configPaths[(int)ConfigFile.User] = path;
            }
        }

        // Check we found everything
        var i = 0;
        foreach (var path in configPaths)
        {
            if (path == null || path == "")
            {
                return false;
            }
            i++;
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
}