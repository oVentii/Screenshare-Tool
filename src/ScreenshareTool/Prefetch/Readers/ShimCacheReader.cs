using System.Text;
using System.Text.Json.Serialization;
















internal sealed class ShimCacheReader
{
    public sealed class ShimCacheEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; init; } = "";
        [JsonPropertyName("pathLower")]
        public string PathLower { get; init; } = "";
        [JsonPropertyName("modified")]
        public string? Modified { get; init; }
        [JsonPropertyName("hasTimestamp")]
        public bool HasTimestamp { get; init; }
        [JsonPropertyName("execFlag")]
        public bool ExecFlag { get; init; }
        [JsonPropertyName("source")]
        public string Source { get; init; } = "";
        [JsonPropertyName("format")]
        public string Format { get; init; } = "";
        [JsonPropertyName("cacheOrder")]
        public int CacheOrder { get; init; }
        [JsonPropertyName("cacheIndex")]
        public int CacheIndex { get; init; }
        [JsonPropertyName("entrySize")]
        public int EntrySize { get; init; }
        [JsonPropertyName("numEntries")]
        public int NumEntries { get; init; }
        [JsonPropertyName("badPath")]
        public bool BadPath { get; init; }
    }

    
    private const int Win8StatsSize = 0x80;

    
    private const int Win10EntryMeta = 12;

    private static readonly string[] BadPatterns =
    {
        "\\temp\\", "\\tmp\\", "\\appdata\\local\\temp\\", "\\appdata\\roaming\\",
        "\\downloads\\", "\\users\\public\\", "\\windows\\temp\\",
        "\\programdata\\", "\\$recycle.bin\\", "\\perflogs\\"
    };

    private static string NormalizePath(string p)
    {
        p = p.ToLowerInvariant();
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = p.Substring(4);
        return p.Replace('/', '\\');
    }

    private static bool LooksLikeExecPath(string lower)
        => lower.Contains(".exe") || lower.Contains(".dll") || lower.Contains(".sys") ||
           lower.Contains(".com") || lower.Contains(".bat") || lower.Contains(".cmd") ||
           lower.Contains(".scr");

    private static bool IsBadPath(string lower)
    {
        foreach (var p in BadPatterns)
            if (lower.Contains(p, StringComparison.Ordinal)) return true;
        return false;
    }

    
    
    
    
    public List<ShimCacheEntry> LoadLive()
    {
        var results = new List<ShimCacheEntry>();
        int formatIndex = -1;

        foreach (string subkey in new[]
                 {
                     @"SYSTEM\CurrentControlSet\Control\Session Manager\AppCompatCache",
                     @"SYSTEM\CurrentControlSet\Control\Session Manager\AppCompatibility"
                 })
        {
            if (NativeMethods.RegOpenKeyExW(
                    NativeMethods.HKEY_LOCAL_MACHINE, subkey, 0,
                    NativeMethods.KEY_READ | NativeMethods.KEY_WOW64_64KEY, out IntPtr hKey)
                != NativeMethods.ERROR_SUCCESS)
                continue;

            try
            {
                uint type = 0, size = 0;
                if (NativeMethods.RegQueryValueExW(hKey, "AppCompatCache", IntPtr.Zero, out type, null, ref size)
                        == NativeMethods.ERROR_SUCCESS &&
                    type == NativeMethods.REG_BINARY && size > 0 && size < 64 * 1024 * 1024)
                {
                    byte[] buf = new byte[size];
                    if (NativeMethods.RegQueryValueExW(hKey, "AppCompatCache", IntPtr.Zero, out type, buf, ref size)
                        == NativeMethods.ERROR_SUCCESS)
                    {
                        formatIndex = Parse(buf, results);
                        if (formatIndex >= 0)
                        {
                            var heur = new List<ShimCacheEntry>();
                            ParseHeuristic(buf, heur);
                            var seen = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var e in results) seen.Add(e.PathLower);
                            foreach (var e in heur)
                                if (seen.Add(e.PathLower)) results.Add(e);
                        }
                    }
                }
            }
            finally
            {
                NativeMethods.RegCloseKey(hKey);
            }

            if (results.Count > 0) break;
        }

        
        var unique = new List<ShimCacheEntry>();
        var dedup = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in results)
            if (e.Source == "struct" && dedup.Add(e.PathLower)) unique.Add(e);
        foreach (var e in results)
            if (e.Source != "struct" && dedup.Add(e.PathLower)) unique.Add(e);
        return unique;
    }

    
    
    
    
    private int Parse(byte[] data, List<ShimCacheEntry> outList)
    {
        
        string magic = ReadAscii(data, 0, 4);
        if (data.Length > 0x44 && magic is "00am" or "00sr")
        {
            ParseWin7(data, magic == "00am" ? "Win7" : "Win7-SP1", outList);
            return 3;
        }
        
        if (data.Length > 0x34 + 4 &&
            ReadAscii(data, 0x30, 4) == "10ts")
        {
            ParseWin10(data, 0x30, "Win10", outList);
            return 1;
        }
        
        if (data.Length > 0x34 + 4 &&
            ReadAscii(data, 0x34, 4) == "10ts")
        {
            ParseWin10(data, 0x34, "Win10", outList);
            return 1;
        }
        int scanned = ScanForMagic(data, "10ts", 0x10, 0x80);
        if (scanned >= 0)
        {
            ParseWin10(data, scanned, scanned >= 0x48 ? "Win11" : "Win10", outList);
            if (outList.Count > 0)
                return 1;
        }
        
        if (data.Length > Win8StatsSize + 4 && ReadAscii(data, Win8StatsSize, 4) == "00ts")
        {
            ParseWin8(data, "Win8", outList);
            return 2;
        }
        
        if (data.Length > Win8StatsSize + 4 && ReadAscii(data, Win8StatsSize, 4) == "10ts")
        {
            ParseWin8(data, "Win8.1", outList);
            return 2;
        }
        return -1;
    }

    
    
    
    
    
    
    private void ParseWin8(byte[] data, string format, List<ShimCacheEntry> outList)
    {
        int index = Win8StatsSize;
        int order = 0;
        int startCount = outList.Count;

        while (index + Win10EntryMeta + 4 <= data.Length)
        {
            string magic = ReadAscii(data, index, 4);
            if (magic is not ("00ts" or "10ts")) break;

            int entryStart = index;
            uint entryLen = BitConverter.ToUInt32(data, index + 8);
            if (entryLen < 12 || index + Win10EntryMeta + (int)entryLen > data.Length) break;

            index += Win10EntryMeta;
            int end = index + (int)entryLen;

            ushort pathSize = BitConverter.ToUInt16(data, index);
            index += 2;
            if (pathSize < 2 || pathSize > 4096 || (pathSize & 1) != 0 || index + pathSize > end)
            {
                index = end;
                continue;
            }

            string path = Encoding.Unicode.GetString(data, index, pathSize);
            int nul = path.IndexOf('\0');
            if (nul >= 0) path = path.Substring(0, nul);
            index += pathSize;

            
            if (index + 2 <= end)
            {
                ushort packageLen = BitConverter.ToUInt16(data, index);
                index += 2;
                if (packageLen > 0 && index + packageLen <= end)
                    index += packageLen;
            }

            bool hasTs = false;
            string modified = "";
            bool execFlag = false;

            if (index + 20 <= end)
            {
                uint flags = BitConverter.ToUInt32(data, index);
                ulong low = BitConverter.ToUInt32(data, index + 8);
                ulong high = BitConverter.ToUInt32(data, index + 12);
                ulong filetime = (high << 32) | low;
                index += 20;
                execFlag = (flags & 0x2) != 0;
                if (TryFormatFileTime(filetime, out modified))
                    hasTs = true;
            }

            string lower = NormalizePath(path);
            if (LooksLikeExecPath(lower) || path.Contains('\\'))
                outList.Add(new ShimCacheEntry
                {
                    Path = path,
                    PathLower = lower,
                    Modified = hasTs ? modified : null,
                    HasTimestamp = hasTs,
                    ExecFlag = execFlag,
                    Source = "struct",
                    Format = format,
                    CacheOrder = order++,
                    CacheIndex = order,
                    EntrySize = end - entryStart,
                    NumEntries = 0,
                    BadPath = IsBadPath(lower)
                });

            index = end;
        }

        ApplyEntryCount(outList, startCount);
    }

    
    
    
    
    
    
    private void ParseWin7(byte[] data, string format, List<ShimCacheEntry> outList)
    {
        const int arrayOffset = 0x40;
        int index = arrayOffset;
        int order = 0;
        int startCount = outList.Count;

        while (index + 0x18 + 2 <= data.Length)
        {
            int entryStart = index;
            uint pathSize = BitConverter.ToUInt32(data, index);
            if (pathSize < 2 || pathSize > 4096 || (pathSize & 1) != 0) break;
            if (index + 0x18 + pathSize > data.Length) break;

            ulong filetime = BitConverter.ToUInt64(data, index + 0x10);
            string path = Encoding.Unicode.GetString(data, index + 0x18, (int)pathSize);
            int nul = path.IndexOf('\0');
            if (nul >= 0) path = path.Substring(0, nul);

            bool hasTs = false;
            string modified = "";
            if (TryFormatFileTime(filetime, out modified))
            {
                hasTs = true;
            }

            string lower = NormalizePath(path);
            if (LooksLikeExecPath(lower) || path.Contains('\\'))
                outList.Add(new ShimCacheEntry
                {
                    Path = path,
                    PathLower = lower,
                    Modified = hasTs ? modified : null,
                    HasTimestamp = hasTs,
                    Source = "struct",
                    Format = format,
                    CacheOrder = order++,
                    CacheIndex = order,
                    EntrySize = -1,
                    NumEntries = 0,
                    BadPath = IsBadPath(lower)
                });

            
            long needed = 0x18L + pathSize;
            index += needed <= 0x50 ? 0x50 : (int)((needed + 7) & ~7L);
        }

        ApplyEntryCount(outList, startCount);
    }

    
    
    
    
    
    
    
    
    
    
    private void ParseWin10(byte[] data, int arrayOffset, string format, List<ShimCacheEntry> outList)
    {
        int startCount = outList.Count;
        int index = arrayOffset;
        int order = 0;

        while (index + Win10EntryMeta + 4 <= data.Length)
        {
            if (ReadAscii(data, index, 4) != "10ts") break;

            int entryStart = index;
            uint entryLen = BitConverter.ToUInt32(data, index + 8);
            if (entryLen < 10 || index + Win10EntryMeta + (int)entryLen > data.Length) break;

            index += Win10EntryMeta;
            int end = index + (int)entryLen;

            ushort pathSize = BitConverter.ToUInt16(data, index);
            index += 2;
            if (pathSize < 2 || pathSize > 4096 || (pathSize & 1) != 0 || index + pathSize > end)
            {
                index = end;
                continue;
            }

            string path = Encoding.Unicode.GetString(data, index, pathSize);
            int nul = path.IndexOf('\0');
            if (nul >= 0) path = path.Substring(0, nul);
            index += pathSize;

            bool hasTs = false;
            string modified = "";
            if (index + 8 <= end)
            {
                ulong filetime = BitConverter.ToUInt64(data, index);
                index += 8;
                if (TryFormatFileTime(filetime, out modified))
                    hasTs = true;
            }

            
            bool execFlag = false;
            if (index + 4 <= end)
            {
                int dataSize = BitConverter.ToInt32(data, index);
                if (dataSize > 0 && dataSize < 4096 && index + 4 + dataSize <= end)
                {
                    int dataEnd = index + 4 + dataSize;
                    if (dataSize >= 4)
                        execFlag = BitConverter.ToUInt32(data, dataEnd - 4) == 1;
                    index = dataEnd;
                }
                else
                {
                    index = end;
                }
            }

            string lower = NormalizePath(path);
            if (LooksLikeExecPath(lower) || path.Contains('\\'))
                outList.Add(new ShimCacheEntry
                {
                    Path = path,
                    PathLower = lower,
                    Modified = hasTs ? modified : null,
                    HasTimestamp = hasTs,
                    ExecFlag = execFlag,
                    Source = "struct",
                    Format = format,
                    CacheOrder = order++,
                    CacheIndex = order,
                    EntrySize = end - entryStart,
                    NumEntries = 0,
                    BadPath = IsBadPath(lower)
                });

            index = end;
        }

        ApplyEntryCount(outList, startCount);
    }

    
    
    
    
    
    private static void ApplyEntryCount(List<ShimCacheEntry> outList, int startCount)
    {
        int total = outList.Count - startCount;
        if (total <= 0) return;
        for (int i = startCount; i < outList.Count; i++)
            outList[i] = new ShimCacheEntry
            {
                Path = outList[i].Path,
                PathLower = outList[i].PathLower,
                Modified = outList[i].Modified,
                HasTimestamp = outList[i].HasTimestamp,
                ExecFlag = outList[i].ExecFlag,
                Source = outList[i].Source,
                Format = outList[i].Format,
                CacheOrder = outList[i].CacheOrder,
                CacheIndex = outList[i].CacheIndex,
                EntrySize = outList[i].EntrySize,
                NumEntries = total,
                BadPath = outList[i].BadPath
            };
    }

    
    
    
    
    
    private void ParseHeuristic(byte[] data, List<ShimCacheEntry> outList)
    {
        if (data.Length < 8) return;
        int order = 100000;
        int wcharCount = data.Length / 2;
        int i = 0;
        while (i + 4 < wcharCount)
        {
            char c0 = BitConverter.ToChar(data, i * 2);
            char c1 = BitConverter.ToChar(data, (i + 1) * 2);
            char c2 = BitConverter.ToChar(data, (i + 2) * 2);
            bool driveLetter = (c0 is >= 'A' and <= 'Z') || (c0 is >= 'a' and <= 'z');
            if (driveLetter && c1 == ':' && c2 == '\\')
            {
                int start = i, end = i;
                while (end < wcharCount && end * 2 + 1 < data.Length)
                {
                    char ch = BitConverter.ToChar(data, end * 2);
                    if (ch == '\0' || ch < 32 || ch >= 0xD800) break;
                    end++;
                }
                if (end > start + 4 && end - start < 512)
                {
                    string path = Encoding.Unicode.GetString(data, start * 2, (end - start) * 2);
                    if (LooksLikeExecPath(NormalizePath(path)))
                    {
                        string lower = NormalizePath(path);
                        outList.Add(new ShimCacheEntry
                        {
                            Path = path,
                            PathLower = lower,
                            Source = "heuristic",
                            Format = "heuristic",
                            CacheOrder = order++,
                            BadPath = IsBadPath(lower)
                        });
                        i = end;
                        continue;
                    }
                }
            }
            i++;
        }
    }

    private static int ScanForMagic(byte[] data, string magic, int from, int to)
    {
        int end = Math.Min(data.Length - 4, to);
        for (int i = from; i <= end; i += 4)
        {
            if (ReadAscii(data, i, 4) == magic)
                return i;
        }
        return -1;
    }

    private static bool TryFormatFileTime(ulong filetime, out string local)
    {
        local = "";
        if (filetime < 0x01A0000000000000UL || filetime > 0x01F0000000000000UL)
            return false;
        try
        {
            var dt = DateTime.FromFileTimeUtc((long)filetime).ToLocalTime();
            if (dt.Year < 1990 || dt.Year > DateTime.Now.Year + 2)
                return false;
            local = dt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadAscii(byte[] d, int offset, int len)
    {
        if (offset + len > d.Length) return "";
        return Encoding.ASCII.GetString(d, offset, len);
    }
}
