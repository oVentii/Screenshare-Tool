using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;














internal static class RawHiveReader
{
    private const int ReadChunkBytes = 1024 * 1024;   
    private const long MaxHiveBytes = 384L * 1024 * 1024;

    public static bool TryReadHiveBytes(string hivePath, CancellationToken ct, out byte[] data, out string error)
    {
        data = Array.Empty<byte>();
        error = "";

        
        
        
        IntPtr hFile = NativeMethods.CreateFileW(
            hivePath, 0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (hFile == NativeMethods.InvalidHandleValue)
        {
            error = $"Could not open {hivePath} for extents (win32 {NativeMethods.GetLastError()}).";
            return false;
        }

        try
        {
            char drive = ForensicUtil.GetWindowsDriveLetter();
            var extents = QueryExtents(hFile, drive, ct);
            if (extents.Count == 0)
            {
                error = "The hive reported no readable extents.";
                return false;
            }
            if (!TryGetClusterSize(drive, out uint clusterSize))
            {
                error = "Could not determine the volume cluster size.";
                return false;
            }

            string volumePath = @"\\.\" + drive + ":";
            IntPtr hVolume = NativeMethods.CreateFileW(
                volumePath, NativeMethods.GENERIC_READ,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (hVolume == NativeMethods.InvalidHandleValue)
            {
                error = "Could not open the raw volume for reading.";
                return false;
            }
            try
            {
                data = ReadExtents(hVolume, extents, clusterSize, ct);
                return data.Length > 0;
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

    
    
    
    
    
    public static bool TryReadHiveToDir(
        string hivePath, string destDir, CancellationToken ct,
        out string loadPath, out string error)
    {
        loadPath = "";
        error = "";
        try
        {
            if (!TryReadHiveBytes(hivePath, ct, out byte[] bytes, out error))
                return false;
            loadPath = Path.Combine(destDir, Path.GetFileName(hivePath));
            File.WriteAllBytes(loadPath, bytes);

            
            
            
            foreach (string suffix in new[] { ".LOG", ".LOG1", ".LOG2" })
            {
                string src = hivePath + suffix;
                try
                {
                    if (File.Exists(src) &&
                        TryReadHiveBytes(src, ct, out byte[] logBytes, out _))
                    {
                        File.WriteAllBytes(loadPath + suffix, logBytes);
                    }
                }
                catch
                {
                    
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static List<(ulong StartVcn, ulong Count, ulong Lcn)> QueryExtents(
        IntPtr hFile, char drive, CancellationToken ct)
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
                if (offset + 16 > returned)
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
                drive + @":\", out uint spc, out uint bps, out _, out _))
        {
            return false;
        }
        clusterSize = spc * bps;
        return true;
    }

    private static byte[] ReadExtents(
        IntPtr hVolume, List<(ulong StartVcn, ulong Count, ulong Lcn)> extents,
        uint clusterSize, CancellationToken ct)
    {
        ulong totalBytes = 0;
        foreach (var (_, count, _) in extents)
            totalBytes += count * clusterSize;
        if (totalBytes == 0 || totalBytes > (ulong)MaxHiveBytes)
            return Array.Empty<byte>();

        var output = new byte[(int)totalBytes];
        int written = 0;
        var chunk = new byte[Math.Min((uint)ReadChunkBytes, clusterSize)];

        foreach (var (_, extentCount, lcn) in extents)
        {
            for (ulong done = 0; done < extentCount;)
            {
                ct.ThrowIfCancellationRequested();

                
                
                ulong remaining = extentCount - done;
                int clustersPerChunk = Math.Max(1, chunk.Length / (int)clusterSize);
                uint thisChunkClusters = (uint)Math.Min((long)remaining, clustersPerChunk);
                uint bytesThisRead = thisChunkClusters * clusterSize;

                ulong offset = (lcn + done) * clusterSize;
                if (!NativeMethods.SetFilePointerEx(
                        hVolume, (long)offset, out _, NativeMethods.FILE_BEGIN))
                {
                    return Array.Empty<byte>();
                }

                if (!NativeMethods.ReadFile(
                        hVolume, chunk, bytesThisRead, out uint read, IntPtr.Zero))
                {
                    return Array.Empty<byte>();
                }
                if (read > 0)
                {
                    Array.Copy(chunk, 0, output, written, read);
                    written += (int)read;
                }
                done += thisChunkClusters;
            }
        }
        Array.Resize(ref output, written);
        return output;
    }
}
