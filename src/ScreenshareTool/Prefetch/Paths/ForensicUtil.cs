using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;







internal static class ForensicUtil
{
    
    public static bool IsWow64Process
        => Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess;
    public const string UnresolvedPath = "No path found...";

    public const long MinExecUnixTime = 315532800L;    
    public const long MaxExecUnixTime = 4102444800L;   

    private const ulong HundredNsPerSec = 10_000_000UL;
    private const ulong EpochDiffSeconds = 11_644_473_600UL;

    private static readonly object LogonGate = new();
    private static long _cachedLogonUnix;
    private static long _cachedLogonAtTick;

    public static string GetWindowsDirectory()
    {
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(dir))
                return dir;
        }
        catch
        {
            
        }
        return @"C:\Windows";
    }

    public static char GetWindowsDriveLetter()
    {
        string windows = GetWindowsDirectory();
        return windows.Length > 0 && char.IsAsciiLetter(windows[0])
            ? char.ToUpperInvariant(windows[0])
            : 'C';
    }

    public static string GetPrefetchDirectory()
        => Path.Combine(GetWindowsDirectory(), "Prefetch");

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";

        string p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal))
            p = p[4..];
        else if (p.StartsWith(@"\??\", StringComparison.Ordinal))
            p = p[4..];
        return p.ToLowerInvariant();
    }

    public static bool IsUnresolved(string? path)
        => string.IsNullOrWhiteSpace(path) ||
           string.Equals(path, UnresolvedPath, StringComparison.Ordinal);

    public static bool LooksDevicePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsUnresolved(path))
            return false;
        return path.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"Device\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\VOLUME{", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"Volume{", StringComparison.OrdinalIgnoreCase);
    }

    public static long FileTimeToUnixTime(ulong filetime)
    {
        if (filetime < EpochDiffSeconds * HundredNsPerSec)
            return 0;
        return (long)(filetime / HundredNsPerSec - EpochDiffSeconds);
    }

    public static bool IsPlausibleUnixTime(long unix)
        => unix >= MinExecUnixTime && unix <= MaxExecUnixTime;

    public static string UnixTimeToLocalString(long unix)
    {
        if (!IsPlausibleUnixTime(unix))
            return "";
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime
                .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }

    public static string FileTimeToLocalString(ulong filetime)
    {
        if (filetime == 0)
            return "";
        try
        {
            return DateTime.FromFileTimeUtc((long)filetime).ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }

    
    
    
    
    
    public static long GetLogonUnixTime()
    {
        long now = Environment.TickCount64;
        lock (LogonGate)
        {
            if (_cachedLogonUnix != 0 && now - _cachedLogonAtTick < 30_000)
                return _cachedLogonUnix;
        }

        long computed = ComputeLogonUnixTime();
        lock (LogonGate)
        {
            _cachedLogonUnix = computed;
            _cachedLogonAtTick = now;
        }
        return computed;
    }

    public static void InvalidateLogonCache()
    {
        lock (LogonGate)
        {
            _cachedLogonUnix = 0;
            _cachedLogonAtTick = 0;
        }
    }

    private static long ComputeLogonUnixTime()
    {
        try
        {
            int sessionId = Process.GetCurrentProcess().SessionId;
            long earliest = long.MaxValue;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.SessionId != sessionId)
                        continue;
                    long t = new DateTimeOffset(p.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
                    if (t > 0 && t < earliest)
                        earliest = t;
                }
                catch
                {
                    
                }
                finally
                {
                    p.Dispose();
                }
            }
            return earliest == long.MaxValue ? 0 : earliest;
        }
        catch
        {
            return 0;
        }
    }

    public static byte[]? ReadAllBytesBounded(string path, int maxBytes, FileShare share = FileShare.ReadWrite | FileShare.Delete)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, share, 0x4000, FileOptions.SequentialScan);
            if (fs.Length <= 0 || fs.Length > maxBytes)
                return null;
            byte[] data = new byte[fs.Length];
            fs.ReadExactly(data);
            return data;
        }
        catch
        {
            return null;
        }
    }

    
    
    
    
    public static DateTime GetBootTimeUtc()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Windows");
            byte[]? raw = key?.GetValue("SystemStartTime") as byte[];
            if (raw is { Length: >= 8 })
            {
                long ft = BitConverter.ToInt64(raw, 0);
                if (ft > 0)
                    return DateTime.FromFileTimeUtc(ft);
            }
        }
        catch
        {
            
        }

        return DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    public static DateTime GetBootTimeLocal() => GetBootTimeUtc().ToLocalTime();

    
    
    
    
    public static string GetNativeSystem32()
    {
        string windows = GetWindowsDirectory();
        return IsWow64Process
            ? Path.Combine(windows, "Sysnative")
            : Path.Combine(windows, "System32");
    }

    public static IEnumerable<string> EnumerateExistingPathCandidates(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in BuildPathCandidates(path))
        {
            if (!seen.Add(candidate))
                continue;
            yield return candidate;
        }
    }

    
    
    
    
    
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ResolveCache =
        new(StringComparer.OrdinalIgnoreCase);
    private const int ResolveCacheLimit = 60_000;
    private static readonly object ResolveCacheGate = new();

    
    
    public static void ClearPathCaches() => ResolveCache.Clear();

    
    
    
    
    
    public static string ResolveExistingPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsUnresolved(path))
            return path;
        if (ResolveCache.TryGetValue(path, out string? cached))
            return cached.Length == 0 ? path : cached;

        string dos = VolumeMapper.ToDosPath(path);
        string found = "";
        foreach (string candidate in EnumerateExistingPathCandidates(dos))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    found = candidate;
                    break;
                }
            }
            catch
            {
                
            }
        }

        lock (ResolveCacheGate)
        {
            if (ResolveCache.Count >= ResolveCacheLimit)
                ResolveCache.Clear();
            ResolveCache[path] = found;
        }
        return found.Length == 0 ? path : found;
    }

    public static bool FileExistsNative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsUnresolved(path))
            return false;
        if (ResolveCache.TryGetValue(path, out string? cached))
            return cached.Length != 0;
        ResolveExistingPath(path);
        return ResolveCache.TryGetValue(path, out cached) && cached.Length != 0;
    }

    
    
    
    
    public static bool IsOsBinaryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsUnresolved(path))
            return false;

        string n = path.Replace('/', '\\');
        if (n.StartsWith(@"\\?\", StringComparison.Ordinal))
            n = n[4..];
        if (n.StartsWith(@"\??\", StringComparison.Ordinal))
            n = n[4..];

        string windows = GetWindowsDirectory().TrimEnd('\\') + "\\";
        if (!n.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
            return false;

        string rest = n[windows.Length..];
        int slash = rest.IndexOf('\\');
        string first = slash < 0 ? rest : rest[..slash];
        return first.Equals("System32", StringComparison.OrdinalIgnoreCase)
            || first.Equals("SysWOW64", StringComparison.OrdinalIgnoreCase)
            || first.Equals("Sysnative", StringComparison.OrdinalIgnoreCase)
            || first.Equals("WinSxS", StringComparison.OrdinalIgnoreCase)
            || first.Equals("servicing", StringComparison.OrdinalIgnoreCase)
            || first.Equals("SystemApps", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> GetAmcacheHivePaths()
    {
        string windows = GetWindowsDirectory();
        yield return Path.Combine(windows, "AppCompat", "Programs", "Amcache.hve");
        yield return Path.Combine(windows, "appcompat", "Programs", "Amcache.hve");
        if (IsWow64Process)
        {
            string native = GetNativeSystem32();
            string nativeWin = Path.GetDirectoryName(native) ?? windows;
            yield return Path.Combine(nativeWin, "AppCompat", "Programs", "Amcache.hve");
        }
    }

    public static string GetProgramFiles()
        => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public static string GetProgramFilesX86()
    {
        string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return string.IsNullOrEmpty(x86) ? GetProgramFiles() : x86;
    }

    
    
    
    
    public static int GetServicePid(string serviceName, out bool sharedHost)
    {
        sharedHost = false;
        if (string.IsNullOrEmpty(serviceName))
            return 0;

        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
            return 0;
        try
        {
            IntPtr svc = OpenService(scm, serviceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero)
                return 0;
            try
            {
                var status = new SERVICE_STATUS_PROCESS();
                if (QueryServiceStatusEx(svc, SC_STATUS_PROCESS_INFO,
                    ref status, (uint)Marshal.SizeOf<SERVICE_STATUS_PROCESS>(), out _))
                {
                    sharedHost = (status.dwServiceType & SERVICE_WIN32_SHARE_PROCESS) != 0;
                    return (int)status.dwProcessId;
                }
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
        return 0;
    }

    private static IEnumerable<string> BuildPathCandidates(string path)
    {
        string n = VolumeMapper.ToDosPath(path).Replace('/', '\\');
        yield return n;

        if (!IsWow64Process)
            yield break;

        if (ContainsIgnoreCase(n, @"\Windows\System32\"))
        {
            yield return ReplaceIgnoreCase(n, @"\Windows\System32\", @"\Windows\Sysnative\");
            yield return ReplaceIgnoreCase(n, @"\Windows\System32\", @"\Windows\SysWOW64\");
        }
        if (ContainsIgnoreCase(n, @"\Windows\SysWOW64\"))
        {
            yield return ReplaceIgnoreCase(n, @"\Windows\SysWOW64\", @"\Windows\System32\");
            yield return ReplaceIgnoreCase(n, @"\Windows\SysWOW64\", @"\Windows\Sysnative\");
        }
        if (ContainsIgnoreCase(n, @"\Program Files (x86)\"))
            yield return ReplaceIgnoreCase(n, @"\Program Files (x86)\", @"\Program Files\");
        else if (ContainsIgnoreCase(n, @"\Program Files\"))
            yield return ReplaceIgnoreCase(n, @"\Program Files\", @"\Program Files (x86)\");
    }

    private static bool ContainsIgnoreCase(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string ReplaceIgnoreCase(string input, string oldValue, string newValue)
    {
        int i = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return input;
        return string.Concat(input.AsSpan(0, i), newValue, input.AsSpan(i + oldValue.Length));
    }

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_WIN32_SHARE_PROCESS = 0x00000020;
    private const int SC_STATUS_PROCESS_INFO = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenSCManagerW")]
    private static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenServiceW")]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "QueryServiceStatusEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        IntPtr hService, int InfoLevel, ref SERVICE_STATUS_PROCESS lpServiceStatus,
        uint cbBufSize, out uint pcbBytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "CloseServiceHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);
}
