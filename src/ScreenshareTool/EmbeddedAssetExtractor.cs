using System.IO;
using System.Reflection;

internal static class EmbeddedAssetExtractor
{
    private static readonly object Sync = new();
    private static volatile string? _root;

    public static string Root
    {
        get
        {
            if (_root is not null) return _root;
            lock (Sync)
            {
                if (_root is not null) return _root;
                _root = Extract();
            }
            return _root;
        }
    }

    private static string Extract()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "iRis-Screenshare-Tool", "embedded");
        try
        {
            Directory.CreateDirectory(root);
        }
        catch
        {
            root = Path.Combine(AppContext.BaseDirectory, "extracted");
            Directory.CreateDirectory(root);
        }

        // Environment.ProcessPath works inside single-file bundles, where
        // Assembly.Location is always empty.
        string loc = Environment.ProcessPath ?? "";
        DateTime stampUtc = !string.IsNullOrEmpty(loc) && File.Exists(loc)
            ? File.GetLastWriteTimeUtc(loc)
            : DateTime.UtcNow;
        string version = "v" + stampUtc.Ticks.ToString();
        string markerPath = Path.Combine(root, ".embedded-version");

        if (File.Exists(Path.Combine(root, "Assets", "index.html")) &&
            File.Exists(markerPath) && File.ReadAllText(markerPath) == version)
        {
            return root;
        }
        TryDeleteTree(root);

        Directory.CreateDirectory(root);

        var asm = Assembly.GetExecutingAssembly();
        foreach (string name in asm.GetManifestResourceNames())
        {
            string? rel = ResolveRelative(name);
            if (rel is null) continue;

            string target = Path.Combine(root, rel);
            string fullTarget = Path.GetFullPath(target);
            string fullRoot = Path.GetFullPath(root);
            if (!fullTarget.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
                !fullTarget.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            string? dir = Path.GetDirectoryName(fullTarget);
            if (dir is not null) Directory.CreateDirectory(dir);

            using var input = asm.GetManifestResourceStream(name);
            if (input is null) continue;
            using var output = File.Create(fullTarget);
            input.CopyTo(output);
        }

        try { File.WriteAllText(markerPath, version); } catch { }

        return root;
    }

    private static void TryDeleteTree(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }

    private static string? ResolveRelative(string resourceName)
    {
        const string assetsMarker = ".Assets.";
        int i = resourceName.LastIndexOf(assetsMarker, StringComparison.Ordinal);
        if (i >= 0)
            return "Assets" + Path.DirectorySeparatorChar + resourceName.Substring(i + assetsMarker.Length);
        return null;
    }
}
