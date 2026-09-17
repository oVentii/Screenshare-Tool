using System.Text;
using Serilog;
using System.Text.Json.Serialization;







internal sealed class BamReader
{
    private static readonly ILogger Logger = Log.ForContext<BamReader>();
    public sealed class BamEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; init; } = "";
        [JsonPropertyName("pathLower")]
        public string PathLower { get; init; } = "";
        [JsonPropertyName("lastExecution")]
        public string? LastExecution { get; init; }
    }

    public bool LastLoadSucceeded { get; private set; }

    public List<BamEntry> LoadLive()
    {
        var results = new List<BamEntry>(256);
        LastLoadSucceeded = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string root in new[]
                 {
                     @"SYSTEM\CurrentControlSet\Services\bam\State\UserSettings",
                     @"SYSTEM\CurrentControlSet\Services\dam\State\UserSettings",
                     @"SYSTEM\CurrentControlSet\Services\bam\UserSettings",
                     @"SYSTEM\CurrentControlSet\Services\dam\UserSettings"
                 })
        {
            try
            {
                ReadHive(root, results, seen);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "BAM/DAM read failed for {Root}", root);
            }
        }

        LastLoadSucceeded = results.Count > 0 || ForensicUtil.IsAdministrator();
        return results;
    }

    private static void ReadHive(string root, List<BamEntry> results, HashSet<string> seen)
    {
        if (!OpenKey(root, out IntPtr hRoot))
            return;

        try
        {
            uint sidIndex = 0;
            while (EnumSubkey(hRoot, sidIndex++, out string sid) && sid.Length > 0)
            {
                if (!OpenKey(root + "\\" + sid, out IntPtr hSid))
                    continue;
                try
                {
                    EnumValues(hSid, results, seen);
                }
                finally
                {
                    NativeMethods.RegCloseKey(hSid);
                }
            }
        }
        finally
        {
            NativeMethods.RegCloseKey(hRoot);
        }
    }

    private static void EnumValues(IntPtr hKey, List<BamEntry> results, HashSet<string> seen)
    {
        uint index = 0;
        var name = new StringBuilder(2048);
        while (true)
        {
            uint nameLen = 2048;
            name.Clear();
            uint type = 0, dataLen = 0;
            int rc = NativeMethods.RegEnumValueW(
                hKey, index++, name, ref nameLen, IntPtr.Zero, out type, null, ref dataLen);
            if (rc == NativeMethods.ERROR_NO_MORE_ITEMS)
                break;
            if (rc != NativeMethods.ERROR_SUCCESS && rc != NativeMethods.ERROR_MORE_DATA)
                break;
            if (type != NativeMethods.REG_BINARY || dataLen < 8)
                continue;

            string raw = name.ToString();
            if (raw.Length < 4 || raw.Equals("SequenceNumber", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("Version", StringComparison.OrdinalIgnoreCase))
                continue;

            byte[] buf = new byte[dataLen];
            nameLen = 2048;
            name.Clear();
            if (NativeMethods.RegEnumValueW(
                    hKey, index - 1, name, ref nameLen, IntPtr.Zero, out type, buf, ref dataLen)
                != NativeMethods.ERROR_SUCCESS)
                continue;

            string path = VolumeMapper.ToDosPath(raw);
            string lower = ForensicUtil.NormalizePath(path);
            if (lower.Length == 0 || !seen.Add(lower))
                continue;

            string? when = null;
            if (dataLen >= 8)
            {
                ulong ft = BitConverter.ToUInt64(buf, 0);
                when = ForensicUtil.FileTimeToLocalString(ft);
                if (string.IsNullOrEmpty(when) && dataLen >= 16)
                    when = ForensicUtil.FileTimeToLocalString(BitConverter.ToUInt64(buf, (int)dataLen - 8));
                if (string.IsNullOrEmpty(when))
                    when = null;
            }

            results.Add(new BamEntry
            {
                Path = path,
                PathLower = lower,
                LastExecution = when
            });
        }
    }

    private static bool OpenKey(string path, out IntPtr hKey)
    {
        if (NativeMethods.RegOpenKeyExW(
                NativeMethods.HKEY_LOCAL_MACHINE, path, 0,
                NativeMethods.KEY_READ | NativeMethods.KEY_WOW64_64KEY, out hKey)
            == NativeMethods.ERROR_SUCCESS)
            return true;
        return NativeMethods.RegOpenKeyExW(
                   NativeMethods.HKEY_LOCAL_MACHINE, path, 0,
                   NativeMethods.KEY_READ, out hKey)
               == NativeMethods.ERROR_SUCCESS;
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
