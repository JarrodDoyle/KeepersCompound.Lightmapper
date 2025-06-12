namespace KeepersCompound.LGS;

public class PathUtils
{
    public static string ConvertSeparator(string path)
    {
        return path.Replace('\\', '/');
    }
}