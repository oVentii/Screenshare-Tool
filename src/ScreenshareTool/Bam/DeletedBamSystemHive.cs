using System.IO;
using System.Text;











internal static class DeletedBamSystemHive
{
    private static readonly byte[] SearchMarker = Encoding.ASCII.GetBytes("\\Device\\HarddiskVolume");
    private static readonly byte[] EndMarker = Encoding.ASCII.GetBytes(".exe");
    private const long MaxHiveBytes = 192L * 1024 * 1024;

    
    
    
    
    
    
    
    
    public static (List<string> Paths, string? Error) ReadDeletedPaths(CancellationToken ct = default)
    {
        var raw = ReadSystemHiveRaw(ct);
        if (raw is null)
            return (new List<string>(), "Could not read the SYSTEM hive raw data.");

        return (ExtractExecutablePaths(raw), null);
    }

    private static byte[]? ReadSystemHiveRaw(CancellationToken ct)
    {
        string hive = Path.Combine(ForensicUtil.GetWindowsDirectory(), "System32", "config", "SYSTEM");
        char drive = ForensicUtil.GetWindowsDriveLetter();

        
        
        
        
        
        IntPtr hFile = NativeMethods.CreateFileW(
            hive, 0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (hFile == NativeMethods.InvalidHandleValue)
            return null;
        try
        {
            var extents = QueryExtents(hFile, ct);
            if (extents.Count == 0)
                return null;

            if (!TryGetClusterSize(drive, out uint clusterSize))
                return null;

            string volumePath = @"\\.\" + drive + ":";
            IntPtr hVolume = NativeMethods.CreateFileW(
                volumePath, NativeMethods.GENERIC_READ,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (hVolume == NativeMethods.InvalidHandleValue)
                return null;
            try
            {
                return ReadExtents(hVolume, extents, clusterSize, ct);
            }
            finally
            {
                NativeMethods.CloseHandle(hVolume);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(hFile);
        }
    }

    
    
    
    
    
    
    private static List<(ulong StartVcn, ulong Count, ulong Lcn)> QueryExtents(IntPtr hFile, CancellationToken ct)
    {
        var result = new List<(ulong, ulong, ulong)>();
        const int BufSize = 64 * 1024;
        byte[] outBuf = new byte[BufSize];

        long queryVcn = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            byte[] inBuf = new byte[8];
            BitConverter.TryWriteBytes(inBuf, queryVcn);

            bool ok = NativeMethods.DeviceIoControl(
                hFile, NativeMethods.FSCTL_GET_RETRIEVAL_POINTERS,
                inBuf, (uint)inBuf.Length, outBuf, BufSize, out uint returned, IntPtr.Zero);

            if (!ok && NativeMethods.GetLastError() != NativeMethods.ERROR_MORE_DATA)
                return result;
            if (returned < 16)
                break;

            long extentCount = BitConverter.ToInt64(outBuf, 0);
            long startingVcn = BitConverter.ToInt64(outBuf, 8);
            int offset = 16;
            long prevVcn = startingVcn;
            long lastNextVcn = startingVcn;
            for (int i = 0; i < extentCount; i++)
            {
                if (offset + 16 > returned || offset + 16 > outBuf.Length)
                    break;
                long nextVcn = BitConverter.ToInt64(outBuf, offset);
                long lcn = BitConverter.ToInt64(outBuf, offset + 8);
                offset += 16;
                lastNextVcn = nextVcn;
                if (lcn >= 0)
                    result.Add(((ulong)prevVcn, (ulong)(nextVcn - prevVcn), (ulong)lcn));
                
                
                prevVcn = nextVcn;
            }

            if (ok)
                break; 

            queryVcn = lastNextVcn;
        }
        return result;
    }

    private static bool TryGetClusterSize(char drive, out uint clusterSize)
    {
        clusterSize = 0;
        if (!NativeMethods.GetDiskFreeSpaceW(
                drive + ":\\", out uint spc, out uint bps, out _, out _))
        {
            return false;
        }
        clusterSize = spc * bps;
        return true;
    }

    private static byte[]? ReadExtents(
        IntPtr hVolume, List<(ulong StartVcn, ulong Count, ulong Lcn)> extents,
        uint clusterSize, CancellationToken ct)
    {
        ulong totalBytes = 0;
        foreach (var (_, count, _) in extents)
            totalBytes += count * clusterSize;
        if (totalBytes == 0 || totalBytes > MaxHiveBytes)
            return null;

        var output = new byte[(int)totalBytes];
        int written = 0;
        
        
        var chunk = new byte[clusterSize];

        foreach (var (_, extentCount, lcn) in extents)
        {
            
            
            ct.ThrowIfCancellationRequested();
            for (ulong i = 0; i < extentCount; i++)
            {
                ulong offset = (lcn + i) * clusterSize;
                if (!NativeMethods.SetFilePointerEx(
                        hVolume, (long)offset, out _, NativeMethods.FILE_BEGIN))
                {
                    return null;
                }

                if (!NativeMethods.ReadFile(
                        hVolume, chunk, clusterSize, out uint read, IntPtr.Zero))
                {
                    return null;
                }
                if (read > 0)
                {
                    Array.Copy(chunk, 0, output, written, read);
                    written += (int)read;
                }
            }
        }
        return output;
    }

    
    
    
    
    
    
    internal static List<string> ExtractExecutablePaths(byte[] hive)
    {
        if (hive is null || hive.Length < SearchMarker.Length)
            return new List<string>();

        var result = BamCoreParser.RecoverDeleted(hive);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recovered in result.RecoveredPaths)
        {
            string dos = VolumeMapper.ToDosPath(recovered.RawPath);
            string lower = ForensicUtil.NormalizePath(dos);
            if (lower.Length > 0)
                found.Add(lower);
        }
        return found.ToList();
    }
}
