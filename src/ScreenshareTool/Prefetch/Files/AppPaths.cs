using System;
using System.IO;







public static class AppPaths
{
    private static readonly string RootValue = Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iRis");

    
    public static string Root => RootValue;

    public static string Downloads => Sub("Downloads");
    public static string Exports => Sub("Exports");
    public static string Cache => Sub("Cache");
    public static string Logs => Sub("Logs");
    public static string Config => Sub("Config");
    public static string Tempx => Sub("Temp");

    
    public static string TempDir(string name) => Sub(Path.Combine("Temp", name));

    
    public static string SubDir(params string[] parts)
        => Combine(RootValue, Path.Combine(parts));

    private static string Sub(string rel) => Combine(RootValue, rel);

    private static string Combine(string root, string rel)
    {
        string path = Path.Combine(root, rel);
        try
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
        catch
        {
            
        }
        return path;
    }
}
