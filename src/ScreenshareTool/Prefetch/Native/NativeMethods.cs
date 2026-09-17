using System.Runtime.InteropServices;
using System.Text;







internal static class NativeMethods
{
    
    
    
    public const ushort COMPRESSION_FORMAT_DEFAULT = 0x0001;
    public const ushort COMPRESSION_FORMAT_LZNT1 = 0x0002;
    public const ushort COMPRESSION_FORMAT_XPRESS = 0x0003;
    public const ushort COMPRESSION_FORMAT_XPRESS_HUFF = 0x0004;

    [DllImport("ntdll.dll", EntryPoint = "RtlGetCompressionWorkSpaceSize")]
    public static extern uint RtlGetCompressionWorkSpaceSize(
        ushort CompressionFormatAndEngine,
        out uint CompressBufferWorkSpaceSize,
        out uint CompressFragmentWorkSpaceSize);

    [DllImport("ntdll.dll", SetLastError = true, EntryPoint = "RtlDecompressBufferEx")]
    public static extern uint RtlDecompressBufferEx(
        ushort CompressionFormat,
        byte[] UncompressedBuffer,
        int UncompressedBufferSize,
        byte[] CompressedBuffer,
        int CompressedBufferSize,
        out int FinalUncompressedSize,
        IntPtr WorkSpace);

    
    [DllImport("kernel32.dll", EntryPoint = "GetLogicalDrives")]
    public static extern uint GetLogicalDrives();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryDosDeviceW")]
    public static extern uint QueryDosDeviceW(
        string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeInformationW")]
    public static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder? lpFileSystemNameBuffer,
        int nFileSystemNameSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumePathNamesForVolumeNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumePathNamesForVolumeNameW(
        string lpszVolumeName,
        [Out] char[] lpszVolumePathNames,
        uint cchBufferLength,
        out uint lpcchReturnLength);

    
    public const int WTD_UI_NONE = 2;
    public const int WTD_REVOKE_NONE = 0;
    public const int WTD_CHOICE_FILE = 1;
    public const int WTD_CHOICE_CATALOG = 2;
    public const int WTD_STATEACTION_VERIFY = 1;
    public const int WTD_STATEACTION_CLOSE = 2;

    
    
    public const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;
    public const uint WTD_LIFETIME_SIGNING_FLAG = 0x00000800;
    public const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;
    public const uint WTD_DISABLE_MD2_MD4 = 0x00002000;

    public const uint WSS_VERIFY_SPECIFIC = 0x00000001;
    public const uint WSS_GET_SECONDARY_SIG_COUNT = 0x00000002;

    public const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    public const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);
    public const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
    public const int CERT_E_EXPIRED = unchecked((int)0x800B0101);
    public const int CERT_E_REVOKED = unchecked((int)0x800B010C);
    public const int NTE_BAD_SIGNATURE = unchecked((int)0x80090006);

    public static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    
    public static readonly Guid DRIVER_ACTION_VERIFY =
        new("F750E6C3-38EE-11d1-85E5-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINTRUST_SIGNATURE_SETTINGS
    {
        public uint cbStruct;
        public uint dwIndex;
        public uint dwFlags;
        public uint cSecondarySigs;
        public uint dwVerifiedSigIndex;
        public IntPtr pCryptoPolicy;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WINTRUST_CATALOG_INFO
    {
        public uint cbStruct;
        public uint dwCatalogVersion;
        public string pcwszCatalogFilePath;
        public string pcwszMemberTag;
        public string pcwszMemberFilePath;
        public IntPtr hMemberFile;
        public IntPtr pbCalculatedFileHash;
        public uint cbCalculatedFileHash;
        public IntPtr pcCatalogContext;
        public IntPtr hCatAdmin;
    }

    [DllImport("wintrust.dll", SetLastError = true, EntryPoint = "WinVerifyTrust")]
    public static extern int WinVerifyTrust(
        IntPtr hwnd,
        ref Guid pgActionID,
        ref WINTRUST_DATA pWVTData);

    
    [DllImport("mscat.dll", SetLastError = true, EntryPoint = "CryptCATAdminAcquireContext")]
    public static extern bool CryptCATAdminAcquireContext(
        out IntPtr phCatAdmin,
        ref Guid pgSubsystem,
        uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CryptCATAdminAcquireContext2")]
    public static extern bool CryptCATAdminAcquireContext2(
        out IntPtr phCatAdmin,
        IntPtr pgSubsystem,
        string? pwszHashAlgorithm,
        IntPtr pStrongHashPolicy,
        uint dwFlags);

    [DllImport("mscat.dll", SetLastError = true, EntryPoint = "CryptCATAdminReleaseContext")]
    public static extern bool CryptCATAdminReleaseContext(
        IntPtr hCatAdmin,
        uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true, EntryPoint = "CryptCATAdminCalcHashFromFileHandle2")]
    public static extern bool CryptCATAdminCalcHashFromFileHandle2(
        IntPtr hCatAdmin,
        IntPtr hFile,
        ref uint pcbHash,
        byte[]? pbHash,
        uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true, EntryPoint = "CryptCATAdminCalcHashFromFileHandle")]
    public static extern bool CryptCATAdminCalcHashFromFileHandle(
        IntPtr hFile,
        ref uint pcbHash,
        byte[]? pbHash,
        uint dwFlags);

    [DllImport("mscat.dll", SetLastError = true, EntryPoint = "CryptCATAdminEnumCatalogFromHash")]
    public static extern IntPtr CryptCATAdminEnumCatalogFromHash(
        IntPtr hCatAdmin,
        byte[] pbHash,
        uint cbHash,
        uint dwFlags,
        ref IntPtr phPrevCatInfo);

    [DllImport("mscat.dll", SetLastError = true, EntryPoint = "CryptCATAdminReleaseCatalogContext")]
    public static extern bool CryptCATAdminReleaseCatalogContext(
        IntPtr hCatAdmin,
        IntPtr hCatInfo,
        uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CATALOG_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string wszCatalogFile;
    }

    [DllImport("mscat.dll", SetLastError = true, EntryPoint = "CryptCATCatalogInfoFromContext")]
    public static extern bool CryptCATCatalogInfoFromContext(
        IntPtr hCatInfo,
        ref CATALOG_INFO psCatInfo,
        uint dwFlags);

    
    public const uint GENERIC_READ = 0x80000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint FILE_SHARE_DELETE = 0x00000004;
    public const uint OPEN_EXISTING = 3;

    public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900f4;
    public const uint FSCTL_READ_USN_JOURNAL = 0x000900bb;

    public const int ERROR_JOURNAL_DELETE_IN_PROGRESS = 1178;
    public const int ERROR_JOURNAL_NOT_ACTIVE = 1179;
    public const int ERROR_JOURNAL_ENTRY_DELETED = 1181;

    
    public const uint USN_REASON_DATA_OVERWRITE = 0x00000001;
    public const uint USN_REASON_DATA_EXTEND = 0x00000002;
    public const uint USN_REASON_DATA_TRUNCATION = 0x00000004;
    public const uint USN_REASON_NAMED_DATA_OVERWRITE = 0x00000010;
    public const uint USN_REASON_NAMED_DATA_EXTEND = 0x00000020;
    public const uint USN_REASON_NAMED_DATA_TRUNCATION = 0x00000040;
    public const uint USN_REASON_FILE_CREATE = 0x00000100;
    public const uint USN_REASON_FILE_DELETE = 0x00000200;
    public const uint USN_REASON_EA_CHANGE = 0x00000400;
    public const uint USN_REASON_SECURITY_CHANGE = 0x00000800;
    public const uint USN_REASON_RENAME_OLD_NAME = 0x00001000;
    public const uint USN_REASON_RENAME_NEW_NAME = 0x00002000;
    public const uint USN_REASON_INDEXABLE_CHANGE = 0x00004000;
    public const uint USN_REASON_BASIC_INFO_CHANGE = 0x00008000;
    public const uint USN_REASON_HARD_LINK_CHANGE = 0x00010000;
    public const uint USN_REASON_COMPRESSION_CHANGE = 0x00020000;
    public const uint USN_REASON_ENCRYPTION_CHANGE = 0x00040000;
    public const uint USN_REASON_OBJECT_ID_CHANGE = 0x00080000;
    public const uint USN_REASON_REPARSE_POINT_CHANGE = 0x00100000;
    public const uint USN_REASON_STREAM_CHANGE = 0x00200000;
    public const uint USN_REASON_CLOSE = 0x80000000;

    public static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct USN_JOURNAL_DATA_V0
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct READ_USN_JOURNAL_DATA_V0
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }

    
    
    
    
    
    [StructLayout(LayoutKind.Sequential)]
    public struct READ_USN_JOURNAL_DATA_V1
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
        public ushort MinMajorVersion;
        public ushort MaxMajorVersion;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    public static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CloseHandle")]
    public static extern bool CloseHandle(IntPtr hObject);

    public static int GetLastError() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    public static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        byte[]? lpInBuffer,
        uint nInBufferSize,
        byte[]? lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    
    public static readonly IntPtr HKEY_LOCAL_MACHINE = new(0x80000002);

    public const uint KEY_READ = 0x00020019;
    public const uint KEY_WOW64_64KEY = 0x00000100;
    public const uint REG_BINARY = 3;
    public const uint REG_SZ = 1;
    public const uint REG_EXPAND_SZ = 2;
    public const uint REG_DWORD = 4;
    public const uint REG_QWORD = 11;
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_MORE_DATA = 234;
    public const int ERROR_HANDLE_EOF = 38;
    public const int ERROR_NO_MORE_ITEMS = 259;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegOpenKeyExW")]
    public static extern int RegOpenKeyExW(
        IntPtr hKey, string lpSubKey, uint ulOptions, uint samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "RegCloseKey")]
    public static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegQueryValueExW")]
    public static extern int RegQueryValueExW(
        IntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType,
        byte[]? lpData, ref uint lpcbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegEnumKeyExW")]
    public static extern int RegEnumKeyExW(
        IntPtr hKey, uint dwIndex, StringBuilder lpName, ref uint lpcchName,
        IntPtr lpReserved, IntPtr lpClass, IntPtr lpcchClass, IntPtr lpftLastWriteTime);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegEnumValueW")]
    public static extern int RegEnumValueW(
        IntPtr hKey, uint dwIndex, StringBuilder lpValueName, ref uint lpcchValueName,
        IntPtr lpReserved, out uint lpType, byte[]? lpData, ref uint lpcbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegLoadKeyW")]
    public static extern int RegLoadKeyW(IntPtr hKey, string lpSubKey, string lpFile);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegUnLoadKeyW")]
    public static extern int RegUnLoadKeyW(IntPtr hKey, string lpSubKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CopyFileW")]
    public static extern bool CopyFileW(string lpExistingFileName, string lpNewFileName, bool bFailIfExists);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "DeleteFileW")]
    public static extern bool DeleteFileW(string lpFileName);

    
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    public const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
    public const int FileIdInfo = 18;

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetFileInformationByHandleEx")]
    public static extern bool GetFileInformationByHandleEx(
        IntPtr hFile, int fileInformationClass, byte[]? lpFileInformation, uint dwBufferSize);

    
    
    
    
    public const uint FSCTL_GET_RETRIEVAL_POINTERS = 0x00090073;
    public const uint FILE_BEGIN = 0;
    public const uint GENERIC_WRITE = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTING_VCN_INPUT_BUFFER
    {
        public long StartingVcn;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RETRIEVAL_POINTERS_BUFFER
    {
        public long ExtentCount;
        public long StartingVcn;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RETRIEVAL_POINTERS_EXTENT
    {
        public long NextVcn;
        public long Lcn;
    }

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFilePointerEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetFilePointerEx(
        IntPtr hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadFile")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadFile(
        IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetDiskFreeSpaceW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetDiskFreeSpaceW(
        string lpRootPathName,
        out uint lpSectorsPerCluster,
        out uint lpBytesPerSector,
        out uint lpNumberOfFreeClusters,
        out uint lpTotalNumberOfClusters);

}
