using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

internal static class ForensicUtil
{
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

    public static byte[]? ReadAllBytesBounded(
        string path, int maxBytes,
        FileShare share = FileShare.ReadWrite | FileShare.Delete)
    {
        try
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, share, 0x4000, FileOptions.SequentialScan);
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
    private static extern IntPtr OpenSCManager(
        string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

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
