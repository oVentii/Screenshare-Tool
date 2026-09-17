using System.Collections.Concurrent;
using System.Globalization;
using System.Text;






internal static class VolumeMapper
{
    private static readonly Dictionary<uint, string> SerialToDrive = new();
    private static readonly Dictionary<string, string> DeviceToDrive = new(StringComparer.OrdinalIgnoreCase);

    
    
    
    private static readonly ConcurrentDictionary<string, string> DosPathCache = new(StringComparer.OrdinalIgnoreCase);
    private const int DosPathCacheLimit = 200_000;

    private static volatile bool _initialized;

    public static void Refresh()
    {
        lock (SerialToDrive)
        {
            SerialToDrive.Clear();
            DeviceToDrive.Clear();
            _initialized = false;
        }
        DosPathCache.Clear();
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (SerialToDrive)
        {
            if (_initialized) return;

            uint drives = NativeMethods.GetLogicalDrives();
            for (int i = 0; i < 26; i++)
            {
                if ((drives & (1u << i)) == 0) continue;

                string root = ((char)('A' + i)) + ":\\";
                var volName = new StringBuilder(64);
                var fsName = new StringBuilder(64);
                string letter = ((char)('A' + i)) + ":";
                if (NativeMethods.GetVolumeInformationW(
                        root, volName, volName.Capacity,
                        out uint serial, out _, out _, fsName, fsName.Capacity))
                {
                    SerialToDrive[serial] = letter;
                }

                var device = new StringBuilder(512);
                if (NativeMethods.QueryDosDeviceW(letter, device, device.Capacity) > 0)
                {
                    string target = device.ToString().TrimEnd('\0');
                    if (target.Length > 0)
                        DeviceToDrive[target] = letter;
                }
            }
            _initialized = true;
        }
    }

    
    
    
    
    public static string ConvertVolumePathToDrive(string path)
    {
        const string volumePrefix = "\\VOLUME{";
        int start = path.IndexOf(volumePrefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            
            
            return path;
        }

        int end = path.IndexOf('}', start);
        if (end < 0) return path;

        int dash = path.LastIndexOf('-', end);
        if (dash < 0 || dash + 1 >= end) return path;

        string serialStr = path.Substring(dash + 1, end - dash - 1);
        if (!uint.TryParse(serialStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint serial))
            return path;

        EnsureInitialized();
        if (!SerialToDrive.TryGetValue(serial, out string? driveLetter))
            return path;

        return driveLetter + path.Substring(end + 1);
    }

    
    
    
    
    public static string ToDosPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        if (DosPathCache.TryGetValue(path, out string? cached))
            return cached;

        string p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal))
            p = p[4..];
        else if (p.StartsWith(@"\??\", StringComparison.Ordinal))
            p = p[4..];

        p = ConvertVolumePathToDrive(p);
        p = ConvertGuidVolumePath(p);

        EnsureInitialized();
        foreach (var kv in DeviceToDrive)
        {
            if (p.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                p = kv.Value + p[kv.Key.Length..];
                break;
            }
        }

        if (DosPathCache.Count >= DosPathCacheLimit)
            DosPathCache.Clear();
        DosPathCache[path] = p;
        return p;
    }

    
    
    
    
    private static string ConvertGuidVolumePath(string path)
    {
        const string marker = "Volume{";
        int start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return path;
        int end = path.IndexOf('}', start);
        if (end < 0)
            return path;

        string guid = path.Substring(start, end - start + 1);
        string volName = @"\\?\" + guid + @"\";
        var buf = new char[260];
        bool ok = NativeMethods.GetVolumePathNamesForVolumeNameW(volName, buf, (uint)buf.Length, out uint needed);
        if (!ok && needed > buf.Length && needed < 4096)
        {
            buf = new char[needed];
            ok = NativeMethods.GetVolumePathNamesForVolumeNameW(volName, buf, needed, out _);
        }
        if (!ok)
            return path;

        int z = Array.IndexOf(buf, '\0');
        string mount = z > 0 ? new string(buf, 0, z) : new string(buf).TrimEnd('\0');
        if (string.IsNullOrEmpty(mount))
            return path;
        mount = mount.TrimEnd('\\');
        return mount + path[(end + 1)..];
    }
}
