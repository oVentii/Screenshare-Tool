using System.Runtime.InteropServices;







internal static class TokenPrivileges
{
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

    public const string SeBackup = "SeBackupPrivilege";
    public const string SeRestore = "SeRestorePrivilege";

    private static int _enabled; 

    public static bool TryEnableBackupRestore()
    {
        int state = System.Threading.Volatile.Read(ref _enabled);
        if (state != 0)
            return state > 0;

        bool ok = Enable(SeBackup, SeRestore);
        System.Threading.Interlocked.CompareExchange(ref _enabled, ok ? 1 : -1, 0);
        return ok;
    }

    public static bool Enable(params string[] names)
    {
        if (names.Length == 0)
            return false;

        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
            return false;

        try
        {
            bool any = false;
            foreach (string name in names)
            {
                if (!LookupPrivilegeValue(null, name, out LUID luid))
                    continue;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    }
                };

                if (AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    
                    
                    
                    if (err != ERROR_NOT_ALL_ASSIGNED)
                        any = true;
                }
            }
            return any;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    
    
    
    
    
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "OpenProcessToken")]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "AdjustTokenPrivileges")]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle, bool disableAll,
        ref TOKEN_PRIVILEGES newState, uint bufferLength,
        IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CloseHandle")]
    private static extern bool CloseHandle(IntPtr handle);
}
