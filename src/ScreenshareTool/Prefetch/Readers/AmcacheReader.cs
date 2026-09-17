using System.IO;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Serilog;
using System.Text.Json.Serialization;








internal sealed class AmcacheReader
{
    private static readonly ILogger Logger = Log.ForContext<AmcacheReader>();
    public sealed class AmcacheEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; init; } = "";
        [JsonPropertyName("pathLower")]
        public string PathLower { get; init; } = "";
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";
        [JsonPropertyName("publisher")]
        public string Publisher { get; init; } = "";
        [JsonPropertyName("binaryType")]
        public string BinaryType { get; init; } = "";
        [JsonPropertyName("originalFileName")]
        public string OriginalFileName { get; init; } = "";
        [JsonPropertyName("productName")]
        public string ProductName { get; init; } = "";
        [JsonPropertyName("companyName")]
        public string CompanyName { get; init; } = "";
        [JsonPropertyName("fileVersion")]
        public string FileVersion { get; init; } = "";
        [JsonPropertyName("programId")]
        public string ProgramId { get; init; } = "";
        [JsonPropertyName("sha1")]
        public string Sha1 { get; init; } = "";
        [JsonPropertyName("linkDate")]
        public string LinkDate { get; init; } = "";
        [JsonPropertyName("size")]
        public long Size { get; init; }
        
        
        
        
        [JsonPropertyName("execUnix")]
        public long ExecUnix { get; init; }
        [JsonPropertyName("lastWrite")]
        public string LastWrite { get; init; } = "";
        [JsonPropertyName("isPe")]
        public bool IsPe { get; init; }
        [JsonPropertyName("isDriver")]
        public bool IsDriver { get; init; }
        [JsonPropertyName("badPath")]
        public bool BadPath { get; init; }
        [JsonPropertyName("hasInstallRecord")]
        public bool HasInstallRecord { get; init; }
    }

    private sealed class ProgramInfo
    {
        public string Name { get; init; } = "";
        public string Publisher { get; init; } = "";
        public string Version { get; init; } = "";
    }

    private static readonly string[] BadPatterns =
    {
        "\\temp\\", "\\tmp\\", "\\appdata\\local\\temp\\", "\\appdata\\roaming\\",
        "\\downloads\\", "\\users\\public\\", "\\windows\\temp\\",
        "\\programdata\\", "\\$recycle.bin\\", "\\appdata\\local\\programs\\", "\\perflogs\\"
    };

    private static readonly string[] PathValueNames =
    {
        "LowerCaseLongPath", "FullPath", "LongPath", "Path", "FilePath",
        "LowerImagePath", "ImagePath"
    };

    private static string NormalizePath(string p)
    {
        p = p.ToLowerInvariant();
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = p.Substring(4);
        return p.Replace('/', '\\');
    }

    private static bool IsBadPath(string lower)
    {
        foreach (var p in BadPatterns)
            if (lower.Contains(p, StringComparison.Ordinal)) return true;
        return false;
    }

    public bool LastLoadSucceeded { get; private set; }

    public List<AmcacheEntry> LoadLive()
    {
        var results = new List<AmcacheEntry>();
        LastLoadSucceeded = false;

        string? hivePath = FindHive();
        if (hivePath is null)
        {
            Logger.Debug("Amcache.hve not found under {Windows}", ForensicUtil.GetWindowsDirectory());
            return results;
        }

        bool priv = TokenPrivileges.TryEnableBackupRestore();
        Logger.Debug("Amcache backup/restore privileges enabled={Enabled}", priv);

        string tempDir = AppPaths.TempDir("amcache-" + Guid.NewGuid().ToString("N"));
        string tempKey = "IRIS_AMCACHE_" + Environment.ProcessId + "_" + Environment.TickCount64;

        try
        {
            Directory.CreateDirectory(tempDir);
            string loadPath = Path.Combine(tempDir, "Amcache.hve");

            
            
            bool mounted = false;
            if (CopyHiveWithLogs(hivePath, loadPath))
            {
                mounted = TryMountAndEnumerate(loadPath, tempKey, out results);
                Logger.Debug("Amcache fast path mounted={Mounted} count={Count}", mounted, results.Count);
            }

            
            
            
            if (!mounted || results.Count == 0)
            {
                results.Clear();
                if (RawHiveReader.TryReadHiveToDir(
                        hivePath, tempDir, CancellationToken.None,
                        out string rawCopy, out string rawError))
                {
                    mounted = TryMountAndEnumerate(rawCopy, tempKey, out results);
                    Logger.Debug("Amcache raw path mounted={Mounted} count={Count}", mounted, results.Count);
                }
                else
                {
                    Logger.Debug("Amcache raw-volume read failed: {Error}", rawError);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Amcache load failed");
        }
        finally
        {
            CleanupDir(tempDir);
        }

        return results;
    }

    private bool TryMountAndEnumerate(string loadPath, string tempKey, out List<AmcacheEntry> found)
    {
        found = new List<AmcacheEntry>();
        NativeMethods.RegUnLoadKeyW(NativeMethods.HKEY_LOCAL_MACHINE, tempKey);
        int lr = NativeMethods.RegLoadKeyW(NativeMethods.HKEY_LOCAL_MACHINE, tempKey, loadPath);
        if (lr != NativeMethods.ERROR_SUCCESS)
        {
            Logger.Debug("RegLoadKey Amcache failed win32={Code} path={Path}", lr, loadPath);
            return false;
        }

        try
        {
            var programs = new Dictionary<string, ProgramInfo>(StringComparer.OrdinalIgnoreCase);
            ReadInstalledPrograms(tempKey + @"\Root\InventoryApplication", programs);
            ReadInstalledPrograms(tempKey + @"\Root\Programs", programs);

            EnumerateSubkeys(tempKey + @"\Root\InventoryApplicationFile", false, programs, found);
            EnumerateSubkeys(tempKey + @"\Root\InventoryDriverBinary", true, programs, found);
            EnumerateLegacyFileKeys(tempKey + @"\Root\File", programs, found);

            LastLoadSucceeded = true;
            return true;
        }
        finally
        {
            NativeMethods.RegUnLoadKeyW(NativeMethods.HKEY_LOCAL_MACHINE, tempKey);
        }
    }

    private static string? FindHive()
    {
        foreach (string candidate in ForensicUtil.GetAmcacheHivePaths())
        {
            try
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                
            }
        }
        return null;
    }

    
    
    
    
    
    
    
    private static bool CopyHiveWithLogs(string hivePath, string destHive)
    {
        if (!CopyLockedFile(hivePath, destHive))
            return false;

        foreach (string suffix in new[] { ".LOG", ".LOG1", ".LOG2" })
        {
            string src = hivePath + suffix;
            try
            {
                if (File.Exists(src))
                    CopyLockedFile(src, destHive + suffix);
            }
            catch
            {
                
            }
        }
        return true;
    }

    private static bool CopyLockedFile(string src, string dst)
    {
        const uint genericRead = 0x80000000;
        const uint share = NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE;
        uint flags = NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_SEQUENTIAL_SCAN;

        IntPtr handle = NativeMethods.CreateFileW(
            src, genericRead, share, IntPtr.Zero, NativeMethods.OPEN_EXISTING, flags, IntPtr.Zero);
        if (handle == NativeMethods.InvalidHandleValue)
        {
            handle = NativeMethods.CreateFileW(
                src, genericRead, share, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        }
        if (handle == NativeMethods.InvalidHandleValue)
            return NativeMethods.CopyFileW(src, dst, false);

        try
        {
            using var srcFs = new FileStream(new SafeFileHandle(handle, ownsHandle: true), FileAccess.Read, 64 * 1024);
            using var dstFs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
            srcFs.CopyTo(dstFs);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Backup copy failed {Src} -> {Dst}", src, dst);
            try { return NativeMethods.CopyFileW(src, dst, false); }
            catch { return false; }
        }
    }

    private static void CleanupDir(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            try
            {
                foreach (string f in Directory.GetFiles(tempDir))
                    NativeMethods.DeleteFileW(f);
                Directory.Delete(tempDir, false);
            }
            catch
            {
                
            }
        }
    }

    private void ReadInstalledPrograms(string baseKey, Dictionary<string, ProgramInfo> programs)
    {
        if (!OpenKey(baseKey, out IntPtr hApp))
            return;

        try
        {
            uint index = 0;
            while (EnumSubkeyName(hApp, index++, out string subName) && subName.Length > 0)
            {
                if (programs.Count >= 8_000)
                    break;
                var info = new ProgramInfo { Name = subName };
                if (OpenKey(baseKey + "\\" + subName, out IntPtr hItem))
                {
                    info = new ProgramInfo
                    {
                        Name = FirstNonEmpty(ReadRegString(hItem, "Name"), subName),
                        Publisher = ReadRegString(hItem, "Publisher"),
                        Version = ReadRegString(hItem, "Version")
                    };
                    string pid = ReadRegString(hItem, "ProgramId");
                    if (pid.Length > 0 && !programs.ContainsKey(pid))
                        programs[pid] = info;
                    NativeMethods.RegCloseKey(hItem);
                }
                if (!programs.ContainsKey(subName))
                    programs[subName] = info;
            }
        }
        finally
        {
            NativeMethods.RegCloseKey(hApp);
        }
    }

    private void EnumerateSubkeys(string baseKey, bool asDriver,
        Dictionary<string, ProgramInfo> programs, List<AmcacheEntry> results)
    {
        if (!OpenKey(baseKey, out IntPtr hParent))
            return;

        try
        {
            uint index = 0;
            while (EnumSubkeyName(hParent, index++, out string subName) && subName.Length > 0)
            {
                if (results.Count >= 12_000)
                    break;
                if (!OpenKey(baseKey + "\\" + subName, out IntPtr hItem))
                    continue;

                try
                {
                    string path = ReadPath(hItem, subName);
                    if (path.Length == 0) continue;

                    string name = FirstNonEmpty(ReadRegString(hItem, "Name"),
                        ReadRegString(hItem, "OriginalFileName"),
                        ReadRegString(hItem, "OriginalFile"),
                        Path.GetFileName(path));
                    string programId = ReadRegString(hItem, "ProgramId");

                    string lower = NormalizePath(path);
                    bool isExec = asDriver ||
                                  lower.Contains(".exe") || lower.Contains(".dll") ||
                                  lower.Contains(".sys") || lower.Contains(".com");
                    if (!isExec) continue;

                    bool hasInstall = programId.Length > 0 && programs.TryGetValue(programId, out _);
                    ProgramInfo? prog = hasInstall && programs.TryGetValue(programId, out var found) ? found : null;
                    if (prog == null && programId.Length == 0 && asDriver)
                    {
                        string baseName = Path.GetFileNameWithoutExtension(lower);
                        if (baseName.Length > 0 && programs.TryGetValue(baseName, out var q))
                        {
                            prog = q;
                            hasInstall = true;
                        }
                    }

                    string publisher = ReadRegString(hItem, "Publisher");
                    if (publisher.Length == 0 && prog != null) publisher = prog.Publisher;

                    string product = ReadRegString(hItem, "ProductName");
                    if (product.Length == 0 && prog != null) product = prog.Name;

                    string linkDate = FirstNonEmpty(
                        ReadRegString(hItem, "LinkDate"),
                        FileTimeValueToLocal(hItem, "LinkDate"),
                        FileTimeValueToLocal(hItem, "CompileTime"));
                    long execUnix = FirstExecUnix(hItem);

                    results.Add(new AmcacheEntry
                    {
                        Path = path,
                        PathLower = lower,
                        Name = name,
                        Publisher = publisher,
                        BinaryType = ReadRegString(hItem, "BinaryType"),
                        OriginalFileName = ReadRegString(hItem, "OriginalFileName"),
                        ProductName = product,
                        CompanyName = ReadRegString(hItem, "CompanyName"),
                        FileVersion = FirstNonEmpty(ReadRegString(hItem, "FileVersion"),
                            prog?.Version ?? ""),
                        ProgramId = programId,
                        Sha1 = FileIdToSha1(FirstNonEmpty(
                            ReadRegString(hItem, "FileId"),
                            ReadRegString(hItem, "FileID"),
                            ReadRegBinaryHex(hItem, "FileId"),
                            ReadRegBinaryHex(hItem, "SHA1"))),
                        LinkDate = linkDate,
                        ExecUnix = execUnix,
                        LastWrite = execUnix > 0 ? ForensicUtil.UnixTimeToLocalString(execUnix) : linkDate,
                        Size = ReadRegQwordOrDword(hItem, "Size"),
                        IsPe = true,
                        IsDriver = asDriver,
                        BadPath = IsBadPath(lower),
                        HasInstallRecord = hasInstall
                    });
                }
                finally
                {
                    NativeMethods.RegCloseKey(hItem);
                }
            }
        }
        finally
        {
            NativeMethods.RegCloseKey(hParent);
        }
    }

    private void EnumerateLegacyFileKeys(
        string baseKey, Dictionary<string, ProgramInfo> programs, List<AmcacheEntry> results)
    {
        if (!OpenKey(baseKey, out IntPtr hVol))
            return;

        try
        {
            uint v = 0;
            while (EnumSubkeyName(hVol, v++, out string volName) && volName.Length > 0)
            {
                if (results.Count >= 12_000)
                    break;
                EnumerateSubkeys(baseKey + "\\" + volName, false, programs, results);
            }
        }
        finally
        {
            NativeMethods.RegCloseKey(hVol);
        }
    }

    private static string ReadPath(IntPtr hItem, string subName)
    {
        foreach (string valueName in PathValueNames)
        {
            string p = ReadRegString(hItem, valueName);
            if (p.Length > 0)
                return p;
        }
        return DecodeSubkeyPath(subName);
    }

    
    
    
    
    private static string DecodeSubkeyPath(string subName)
    {
        if (subName.Length < 4 || subName[1] != '@')
            return "";
        if (!char.IsAsciiLetter(subName[0]))
            return "";
        return subName.Replace('@', '\\');
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

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrEmpty(v)) return v;
        return "";
    }

    private bool EnumSubkeyName(IntPtr hKey, uint index, out string name)
    {
        var sb = new StringBuilder(1024);
        uint len = 1024;
        int res = NativeMethods.RegEnumKeyExW(hKey, index, sb, ref len, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (res == NativeMethods.ERROR_SUCCESS && len > 0)
        {
            name = sb.ToString();
            return true;
        }
        name = "";
        return false;
    }

    private static string ReadRegString(IntPtr hKey, string valueName)
    {
        uint type = 0, size = 0;
        if (NativeMethods.RegQueryValueExW(hKey, valueName, IntPtr.Zero, out type, null, ref size)
                != NativeMethods.ERROR_SUCCESS || size == 0)
            return "";
        if (type != NativeMethods.REG_SZ && type != NativeMethods.REG_EXPAND_SZ)
            return "";

        byte[] buf = new byte[size];
        if (NativeMethods.RegQueryValueExW(hKey, valueName, IntPtr.Zero, out type, buf, ref size)
            != NativeMethods.ERROR_SUCCESS)
            return "";
        return Encoding.Unicode.GetString(buf, 0, (int)size).TrimEnd('\0');
    }

    private static string ReadRegBinaryHex(IntPtr hKey, string valueName)
    {
        uint type = 0, size = 0;
        if (NativeMethods.RegQueryValueExW(hKey, valueName, IntPtr.Zero, out type, null, ref size)
                != NativeMethods.ERROR_SUCCESS || size == 0 || size > 64)
            return "";
        if (type != NativeMethods.REG_BINARY)
            return "";
        byte[] buf = new byte[size];
        if (NativeMethods.RegQueryValueExW(hKey, valueName, IntPtr.Zero, out type, buf, ref size)
            != NativeMethods.ERROR_SUCCESS)
            return "";
        var sb = new StringBuilder((int)size * 2);
        for (int i = 0; i < size; i++)
            sb.Append(buf[i].ToString("x2"));
        return sb.ToString();
    }

    private static string FileTimeValueToLocal(IntPtr hKey, string valueName)
    {
        long raw = ReadRegQwordOrDword(hKey, valueName);
        if (raw <= 0)
            return "";
        try
        {
            return ForensicUtil.FileTimeToLocalString((ulong)raw);
        }
        catch
        {
            return "";
        }
    }

    
    
    
    
    
    
    private static long FirstExecUnix(IntPtr hItem)
    {
        foreach (string valueName in new[] { "LastModifiedTime", "LinkDate", "CompileTime" })
        {
            long raw = ReadRegQwordOrDword(hItem, valueName);
            if (raw > 0)
            {
                long unix = ForensicUtil.FileTimeToUnixTime((ulong)raw);
                if (ForensicUtil.IsPlausibleUnixTime(unix))
                    return unix;
            }

            string date = ReadRegString(hItem, valueName);
            long fromString = TryParseDateStringUnix(date);
            if (fromString > 0)
                return fromString;
        }
        return 0;
    }

    internal static long TryParseDateStringUnix(string date)
    {
        if (string.IsNullOrWhiteSpace(date))
            return 0;
        date = date.Trim();
        if (!DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime parsed)
            && !DateTime.TryParse(date, out parsed))
        {
            return 0;
        }
        if (parsed.Year < 1970 || parsed.Year > 2100)
            return 0;
        try
        {
            long unix = new DateTimeOffset(parsed).ToUnixTimeSeconds();
            return unix > 0 ? unix : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static long ReadRegQwordOrDword(IntPtr hKey, string valueName)
    {
        uint type = 0, size = 0;
        if (NativeMethods.RegQueryValueExW(hKey, valueName, IntPtr.Zero, out type, null, ref size)
                != NativeMethods.ERROR_SUCCESS)
            return 0;

        byte[] buf = new byte[Math.Max(size, 8u)];
        if (NativeMethods.RegQueryValueExW(hKey, valueName, IntPtr.Zero, out type, buf, ref size)
            != NativeMethods.ERROR_SUCCESS)
            return 0;

        if (type == NativeMethods.REG_QWORD && size >= 8)
            return BitConverter.ToInt64(buf, 0);
        if (type == NativeMethods.REG_DWORD && size >= 4)
            return BitConverter.ToUInt32(buf, 0);
        return 0;
    }

    private static string FileIdToSha1(string fileId)
    {
        var hex = new StringBuilder();
        foreach (char c in fileId)
            if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                hex.Append(char.ToLowerInvariant(c));
        string s = hex.ToString();
        if (s.Length >= 44 && s.StartsWith("0000", StringComparison.Ordinal))
            s = s.Substring(4);
        if (s.Length > 40) s = s.Substring(0, 40);
        return s;
    }
}
