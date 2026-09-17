
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;








internal static class FileById
{
    private const uint FILE_READ_DATA = 0x0001;
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    
    
    
    
    
    public static byte[]? ReadDeletedBytes(char driveLetter, ulong fileReference, long maxBytes = 8 * 1024 * 1024)
    {
        if (fileReference == 0) return null;

        using var volume = OpenVolume(driveLetter);
        if (volume == null || volume.IsInvalid) return null;

        var descriptor = MakeDescriptor(fileReference);

        var handle = OpenById(volume, ref descriptor);
        if (handle is null || handle.IsInvalid)
        {
            handle?.Dispose();
            return null;
        }

        try
        {
            using var fs = new FileStream(handle, FileAccess.Read, bufferSize: 0x10000, isAsync: false);
            long len = fs.Length;
            if (len <= 0 || len > maxBytes)
                return null;
            var data = new byte[len];
            fs.ReadExactly(data);
            return data;
        }
        catch
        {
            return null;
        }
    }

    private static FILE_ID_DESCRIPTOR MakeDescriptor(ulong fileReference)
    {
        return new FILE_ID_DESCRIPTOR
        {
            dwSize = 24,
            Type = FILE_ID_TYPE.FileIdType,
            FileId = unchecked((long)fileReference)
        };
    }

    private static SafeFileHandle? OpenById(SafeFileHandle hVolumeHint, ref FILE_ID_DESCRIPTOR lpFileId)
    {
        return OpenFileById(
            hVolumeHint,
            ref lpFileId,
            FILE_READ_DATA | SYNCHRONIZE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            FILE_FLAG_BACKUP_SEMANTICS);
    }

    private static SafeFileHandle? OpenVolume(char driveLetter)
    {
        return CreateFileW(
            $"\\\\.\\{driveLetter}:",
            FILE_READ_ATTRIBUTES | FILE_READ_DATA | SYNCHRONIZE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "OpenFileById")]
    private static extern SafeFileHandle OpenFileById(
        SafeFileHandle hVolumeHint,
        ref FILE_ID_DESCRIPTOR lpFileId,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwFlagsAndAttributes);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct FILE_ID_DESCRIPTOR
    {
        [FieldOffset(0)] public int dwSize;
        [FieldOffset(4)] public FILE_ID_TYPE Type;
        [FieldOffset(8)] public long FileId;
        [FieldOffset(8)] public Guid ObjectId;
    }

    private enum FILE_ID_TYPE
    {
        FileIdType = 0,
        ObjectIdType = 1,
        ExtendedFileIdType = 2,
        MaximumFileIdType
    }
}
