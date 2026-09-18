using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class NativeBootstrapper
{
    private const string ResourceName = "ScreenshareTool.Native.WebView2Loader.dll";

    // Keep in sync with the Microsoft.Web.WebView2 PackageReference version.
    // The loader is extracted into a versioned directory so package updates
    // can never load a stale native DLL.
    private const string WebView2Version = "1.0.2535.41";

    private const string DllFileName = "WebView2Loader.dll";

    private static int _initialized;

    public static void EnsureWebView2Loader()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;

        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "iRis-Screenshare-Tool", "native", WebView2Version);
            string target = Path.Combine(dir, DllFileName);

            byte[]? payload = LoadResource();
            if (payload is null || payload.Length == 0)
                return;

            bool extracted = File.Exists(target) && MatchesLength(target, payload.Length);
            if (!extracted)
            {
                try { Directory.CreateDirectory(dir); }
                catch { return; }

                try
                {
                    File.WriteAllBytes(target, payload);
                    extracted = true;
                }
                catch
                {
                    // Locked by another running instance — fall back to the
                    // file that is already there, if any.
                    extracted = File.Exists(target);
                }

                if (extracted)
                    CleanupOldVersions(dir);
            }

            if (!extracted)
                return;

            // Once loaded under its file name, the WebView2 DllImports bind
            // to the already-loaded module instead of probing the app dir.
            try { NativeLibrary.Load(target); }
            catch { }
        }
        catch
        {
        }
    }

    private static bool MatchesLength(string path, int length)
    {
        try { return new FileInfo(path).Length == length; }
        catch { return false; }
    }

    private static void CleanupOldVersions(string currentDir)
    {
        try
        {
            string? parent = Path.GetDirectoryName(currentDir);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                return;
            foreach (string dir in Directory.GetDirectories(parent))
            {
                if (!string.Equals(dir, currentDir, StringComparison.OrdinalIgnoreCase))
                {
                    try { Directory.Delete(dir, recursive: true); }
                    catch { }
                }
            }
        }
        catch
        {
        }
    }

    private static byte[]? LoadResource()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(ResourceName);
            if (stream is null)
                return null;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
