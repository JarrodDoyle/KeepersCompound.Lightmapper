using Serilog;

namespace KeepersCompound.LGS;

internal enum ConfigFile
{
    Cam,
    CamExt,
    CamMod,
    Install,
    ConfigFileCount
}

public class InstallContext
{
    public bool Valid { get; init; }
    public string FmsDir { get; init; } = "";
    public List<string> Fms { get; init; } = new();
    public List<string> LoadPaths { get; init; } = new();

    public InstallContext(string installPath)
    {
        if (!Directory.Exists(installPath))
        {
            Log.Error("Install directory does not exist: {Path}", installPath);
            return;
        }

        if (!TryGetConfigPaths(installPath, out var configPaths))
        {
            return;
        }

        // We need to know where all the resources are
        var installCfgLines = File.ReadAllLines(configPaths[(int)ConfigFile.Install]);
        var resnamePaths = GetValidInstallPaths(installPath, installCfgLines, "resname_base");
        if (resnamePaths.Count == 0)
        {
            Log.Error("No valid {Var} found", "resname_base");
            return;
        }

        var omPaths = GetValidInstallPaths(installPath, installCfgLines, "load_path");
        if (omPaths.Count == 0)
        {
            Log.Error("No valid {Var} found", "load_path");
            return;
        }

        LoadPaths.AddRange(resnamePaths);
        LoadPaths.AddRange(omPaths);

        var camModLines = File.ReadAllLines(configPaths[(int)ConfigFile.CamMod]);
        FindConfigVar(camModLines, "fm_path", out var fmsPath, "FMs");
        FmsDir = Path.Join(installPath, fmsPath);
        foreach (var dir in Directory.GetDirectories(FmsDir))
        {
            var name = Path.GetFileName(dir);
            Fms.Add(name);
        }

        Valid = true;
    }

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

        includePath = PathUtils.ConvertSeparator(includePath);
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
            var dir = Path.Join(rootPath, PathUtils.ConvertSeparator(path));
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