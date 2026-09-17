using System.Text;





internal interface IBamHiveSource
{
    string SourceName { get; }
    bool KeyExists(string path);
    IEnumerable<string> EnumerateSubKeys(string path);
    
    IReadOnlyList<RegfValue> EnumerateValues(string path);
}








internal sealed class LiveBamHiveSource : IBamHiveSource
{
    
    private const string SystemPath = "SYSTEM";

    public string SourceName => "LiveRegistry";

    public bool KeyExists(string path) => OpenKey(path, out IntPtr h) ? Close(h) : false;

    public IEnumerable<string> EnumerateSubKeys(string path)
    {
        if (!OpenKey(path, out IntPtr hRoot))
            yield break;
        try
        {
            uint index = 0;
            while (EnumSubkey(hRoot, index++, out string name) && name.Length > 0)
                yield return name;
        }
        finally
        {
            NativeMethods.RegCloseKey(hRoot);
        }
    }

    public IReadOnlyList<RegfValue> EnumerateValues(string path)
    {
        var values = new List<RegfValue>();
        if (!OpenKey(path, out IntPtr hKey))
            return values;
        try
        {
            uint index = 0;
            while (true)
            {
                var name = new StringBuilder(2048);
                uint nameLen = 2048;
                uint type = 0, dataLen = 0;
                int rc = NativeMethods.RegEnumValueW(
                    hKey, index, name, ref nameLen, IntPtr.Zero, out type, null, ref dataLen);
                if (rc == NativeMethods.ERROR_NO_MORE_ITEMS)
                    break;
                if (rc != NativeMethods.ERROR_SUCCESS && rc != NativeMethods.ERROR_MORE_DATA)
                    break;
                if (dataLen > 16 * 1024 * 1024)
                {
                    index++;
                    continue; 
                }
                byte[] buf = dataLen > 0 ? new byte[dataLen] : Array.Empty<byte>();
                nameLen = 2048;
                name.Clear();
                if (NativeMethods.RegEnumValueW(
                        hKey, index, name, ref nameLen, IntPtr.Zero, out type, buf, ref dataLen)
                    != NativeMethods.ERROR_SUCCESS)
                {
                    index++;
                    continue;
                }
                values.Add(new RegfValue
                {
                    Name = name.ToString(),
                    Type = type,
                    Data = buf,
                    DataOffset = 0,
                });
                index++;
            }
        }
        finally
        {
            NativeMethods.RegCloseKey(hKey);
        }
        return values;
    }

    private static bool OpenKey(string path, out IntPtr hKey)
    {
        string full = SystemPath + (string.IsNullOrEmpty(path) ? "" : "\\" + path);
        if (NativeMethods.RegOpenKeyExW(
                NativeMethods.HKEY_LOCAL_MACHINE, full, 0,
                NativeMethods.KEY_READ | NativeMethods.KEY_WOW64_64KEY, out hKey)
            == NativeMethods.ERROR_SUCCESS)
            return true;
        return NativeMethods.RegOpenKeyExW(
                   NativeMethods.HKEY_LOCAL_MACHINE, full, 0,
                   NativeMethods.KEY_READ, out hKey)
               == NativeMethods.ERROR_SUCCESS;
    }

    private static bool Close(IntPtr hKey)
    {
        NativeMethods.RegCloseKey(hKey);
        return true;
    }

    private static bool EnumSubkey(IntPtr hKey, uint index, out string name)
    {
        var sb = new StringBuilder(256);
        uint len = 256;
        int res = NativeMethods.RegEnumKeyExW(
            hKey, index, sb, ref len, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (res == NativeMethods.ERROR_SUCCESS && len > 0)
        {
            name = sb.ToString();
            return true;
        }
        name = "";
        return false;
    }
}


internal sealed class OfflineBamHiveSource : IBamHiveSource
{
    private readonly BamRegfReader _reader;
    public string SourceName { get; }

    public OfflineBamHiveSource(BamRegfReader reader, string? sourcePath = null)
    {
        _reader = reader;
        SourceName = sourcePath is null ? "OfflineHive" : $"OfflineHive:{sourcePath}";
    }

    public bool KeyExists(string path) => _reader.GetKey(path) is not null;

    public IEnumerable<string> EnumerateSubKeys(string path) => _reader.EnumerateSubKeys(path);

    public IReadOnlyList<RegfValue> EnumerateValues(string path) => _reader.EnumerateValues(path);
}
