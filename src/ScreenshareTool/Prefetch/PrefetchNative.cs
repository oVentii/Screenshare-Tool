using System.Runtime.InteropServices;

internal static class PrefetchNative
{
    [DllImport("ntdll.dll", EntryPoint = "RtlGetCompressionWorkSpaceSize")]
    internal static extern int GetCompressionWorkSpaceSize(
        ushort compressionFormatAndEngine,
        out uint compressBufferWorkSpaceSize,
        out uint compressFragmentWorkSpaceSize);

    [DllImport("ntdll.dll", EntryPoint = "RtlDecompressBufferEx")]
    internal static extern int DecompressBufferEx(
        ushort compressionFormat,
        [Out] byte[] uncompressedBuffer,
        uint uncompressedBufferSize,
        byte[] compressedBuffer,
        uint compressedBufferSize,
        out uint finalUncompressedSize,
        IntPtr workSpace);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumeInformation(
        string rootPathName,
        IntPtr volumeNameBuffer,
        uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        IntPtr fileSystemNameBuffer,
        uint fileSystemNameBufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeNameForVolumeMountPointW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumeNameForMountPoint(
        string mountPoint,
        System.Text.StringBuilder volumeName,
        uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryDosDeviceW")]
    internal static extern uint QueryDosDevice(
        string deviceName,
        System.Text.StringBuilder targetPath,
        uint maxChars);

    [DllImport("secur32.dll", EntryPoint = "LsaEnumerateLogonSessions")]
    internal static extern uint EnumerateLogonSessions(
        out uint logonSessionCount,
        out IntPtr logonSessionList);

    [DllImport("secur32.dll", EntryPoint = "LsaGetLogonSessionData")]
    internal static extern uint GetLogonSessionData(
        IntPtr logonId,
        out IntPtr sessionData);

    [DllImport("secur32.dll", EntryPoint = "LsaFreeReturnBuffer")]
    internal static extern uint FreeReturnBuffer(IntPtr buffer);

    internal const int InteractiveLogon = 2;
    internal const int RemoteInteractiveLogon = 10;
    internal const int LogonTypeOffset = 64;
    internal const int LogonTimeOffset = 80;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr PgpKeyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr UnionInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr URLReference;
        public uint ProvFlags;
        public uint UIContext;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WinTrustCatalogInfo
    {
        public uint StructSize;
        public uint CatalogVersion;
        public string CatalogFilePath;
        public string MemberTag;
        public string MemberFilePath;
        public IntPtr MemberFileHandle;
        public IntPtr CalculatedFileHash;
        public uint CalculatedFileHashSize;
        public IntPtr CatalogContext;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CatalogInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string CatalogFile;
    }

    internal static readonly Guid ActionGenericVerifyV2 =
        new(0xaac56b, 0xcd44, 0x11d0, 0x8c, 0xc2, 0x0, 0xc0, 0x4f, 0xc2, 0x95, 0xee);

    internal const uint WTD_UI_NONE = 2;
    internal const uint WTD_REVOKE_NONE = 0;
    internal const uint WTD_CHOICE_FILE = 1;
    internal const uint WTD_CHOICE_CATALOG = 2;
    internal const uint WTD_STATEACTION_VERIFY = 1;
    internal const uint WTD_STATEACTION_CLOSE = 2;

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WinVerifyTrust")]
    internal static extern int VerifyTrust(
        IntPtr hwnd,
        [In] ref Guid actionId,
        [In, Out] ref WinTrustData data);

    [DllImport("wintrust.dll", EntryPoint = "WTHelperProvDataFromStateData")]
    internal static extern IntPtr ProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", EntryPoint = "WTHelperGetProvSignerFromChain")]
    internal static extern IntPtr GetProvSignerFromChain(
        IntPtr provData, uint index, bool verifySig, uint cryptoProv);

    [DllImport("wintrust.dll", EntryPoint = "WTHelperGetProvCertFromChain")]
    internal static extern IntPtr GetProvCertFromChain(IntPtr signer, uint index);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProvCert
    {
        public uint StructSize;
        public IntPtr Cert;
        public uint Error;
        public uint ChainElement;
    }

    internal const int CERT_X500_NAME_STR = 3;
    internal const int CERT_NAME_SIMPLE_DISPLAY_TYPE = 4;

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CertNameToStrW")]
    internal static extern int CertNameToStr(
        uint certEncodingType,
        IntPtr name,
        uint strType,
        System.Text.StringBuilder buffer,
        uint bufferSize);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CertGetNameStringW")]
    internal static extern int GetCertNameString(
        IntPtr cert,
        uint nameType,
        uint flags,
        IntPtr typePara,
        System.Text.StringBuilder buffer,
        uint bufferSize);

    internal const int CERT_SHA1_HASH_PROP_ID = 3;
    internal const int CERT_FIND_SHA1_HASH = 0x10000;
    internal const uint X509_ASN_ENCODING = 1;
    internal const uint PKCS_7_ASN_ENCODING = 0x10000;
    internal const uint CERT_STORE_PROV_SYSTEM_W = 10;
    internal const uint CERT_SYSTEM_STORE_CURRENT_USER = 0x10000;
    internal const uint CERT_SYSTEM_STORE_LOCAL_MACHINE = 0x20000;
    internal const uint CERT_STORE_OPEN_EXISTING_FLAG = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CryptHashBlob
    {
        public uint Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, EntryPoint = "CertGetCertificateContextProperty")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCertContextProperty(
        IntPtr cert,
        uint propId,
        IntPtr data,
        ref uint dataSize);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CertOpenStore")]
    internal static extern IntPtr OpenStore(
        uint storeProvider,
        uint encodingType,
        IntPtr cryptoProv,
        uint flags,
        string storeName);

    [DllImport("crypt32.dll", SetLastError = true, EntryPoint = "CertFindCertificateInStore")]
    internal static extern IntPtr FindCertificateInStore(
        IntPtr store,
        uint encodingType,
        uint findFlags,
        uint findType,
        ref CryptHashBlob findPara,
        IntPtr prevCert);

    [DllImport("crypt32.dll", SetLastError = true, EntryPoint = "CertFreeCertificateContext")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FreeCertificateContext(IntPtr cert);

    [DllImport("crypt32.dll", SetLastError = true, EntryPoint = "CertCloseStore")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseStore(IntPtr store, uint flags);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CryptCATAdminAcquireContext")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CatAdminAcquireContext(
        out IntPtr catAdmin,
        ref Guid sysAction,
        uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true, EntryPoint = "CryptCATAdminReleaseContext")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CatAdminReleaseContext(IntPtr catAdmin, uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    internal static extern IntPtr CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    internal const uint GENERIC_READ = 0x80000000;
    internal const uint FILE_SHARE_READ = 1;
    internal const uint FILE_SHARE_WRITE = 2;
    internal const uint FILE_SHARE_DELETE = 4;
    internal const uint OPEN_EXISTING = 3;

    [DllImport("wintrust.dll", SetLastError = true, EntryPoint = "CryptCATAdminCalcHashFromFileHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CatAdminCalcHashFromFileHandle(
        IntPtr fileHandle,
        ref uint hashSize,
        IntPtr hash,
        uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true, EntryPoint = "CryptCATAdminEnumCatalogFromHash")]
    internal static extern IntPtr CatAdminEnumCatalogFromHash(
        IntPtr catAdmin,
        byte[] hash,
        uint hashSize,
        uint dwFlags,
        IntPtr prevCatalog);

    [DllImport("wintrust.dll", SetLastError = true, EntryPoint = "CryptCATCatalogInfoFromContext")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CatCatalogInfoFromContext(
        IntPtr catalog,
        ref CatalogInfo info,
        uint dwFlags);

    [DllImport("wintrust.dll", SetLastError = true, EntryPoint = "CryptCATAdminReleaseCatalogContext")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CatAdminReleaseCatalogContext(
        IntPtr catAdmin,
        IntPtr catalog,
        uint dwFlags);

    internal static readonly Guid DriverActionVerify =
        new(0xf750e6c3, 0x38ee, 0x11d1, 0x85, 0xe5, 0x0, 0xc0, 0x4f, 0xc2, 0x95, 0xee);
}
