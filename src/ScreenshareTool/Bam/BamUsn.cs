using System.Runtime.InteropServices;

internal static class BamUsn
{
    private const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
    private const uint FSCTL_READ_USN_JOURNAL = 0x000900BB;
    private const int ReadBufferBytes = 128 * 1024;
    private const int MaxRecordsPerDrive = 2_000_000;

    [StructLayout(LayoutKind.Sequential)]
    private struct ReadJournalData
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JournalData
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern IntPtr CreateVolumeFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CloseHandle")]
    private static extern bool CloseVolumeHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    private static extern bool QueryJournalIo(
        IntPtr device,
        uint code,
        IntPtr inBuffer,
        uint inSize,
        out JournalData outBuffer,
        int outSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    private static extern bool ReadJournalIo(
        IntPtr device,
        uint code,
        ref ReadJournalData inBuffer,
        int inSize,
        IntPtr outBuffer,
        int outSize,
        out uint bytesReturned,
        IntPtr overlapped);

    internal sealed class Record
    {
        public string Name { get; init; } = "";
        public uint Reason { get; init; }
        public long TimeStamp { get; init; }
    }

    public static List<Record> ReadDriveJournal(
        string rootPath, CancellationToken ct, out bool journalOk)
    {
        var records = new List<Record>(4096);
        journalOk = false;

        IntPtr handle = CreateVolumeFile(
            @"\\.\" + rootPath.TrimEnd('\\'),
            0xC0000000u, 0x3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
        if (handle == new IntPtr(-1))
            return records;

        try
        {
            if (!QueryJournalIo(handle, FSCTL_QUERY_USN_JOURNAL,
                    IntPtr.Zero, 0, out JournalData state,
                    Marshal.SizeOf<JournalData>(), out _, IntPtr.Zero))
                return records;
            journalOk = true;

            var query = new ReadJournalData
            {
                StartUsn = state.FirstUsn,
                ReasonMask = uint.MaxValue,
                ReturnOnlyOnClose = 0,
                Timeout = 0,
                BytesToWaitFor = 0,
                UsnJournalId = state.UsnJournalID
            };

            IntPtr buffer = Marshal.AllocHGlobal(ReadBufferBytes);
            try
            {
                while (records.Count < MaxRecordsPerDrive)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!ReadJournalIo(handle, FSCTL_READ_USN_JOURNAL,
                            ref query, Marshal.SizeOf<ReadJournalData>(),
                            buffer, ReadBufferBytes, out uint returned, IntPtr.Zero))
                        break;
                    if (returned <= 60)
                        break;

                    long nextUsn = Marshal.ReadInt64(buffer, 0);
                    int offset = 8;
                    int remaining = (int)returned - 8;
                    while (remaining > 60)
                    {
                        int recordLen = Marshal.ReadInt32(buffer, offset);
                        if (recordLen < 60 || recordLen > remaining)
                            break;
                        Record? rec = ParseRecord(buffer, offset, recordLen);
                        if (rec is not null)
                            records.Add(rec);
                        offset += recordLen;
                        remaining -= recordLen;
                    }

                    if (nextUsn >= state.NextUsn)
                        break;
                    query.StartUsn = nextUsn;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseVolumeHandle(handle);
        }

        return records;
    }

    private static Record? ParseRecord(IntPtr buffer, int offset, int recordLen)
    {
        try
        {
            uint reason = (uint)Marshal.ReadInt32(buffer, offset + 40);
            long timestamp = Marshal.ReadInt64(buffer, offset + 32);
            ushort nameLen = (ushort)Marshal.ReadInt16(buffer, offset + 56);
            ushort nameOff = (ushort)Marshal.ReadInt16(buffer, offset + 58);
            if (nameLen == 0 || (uint)nameOff + nameLen > (uint)recordLen)
                return null;
            string name = Marshal.PtrToStringUni(IntPtr.Add(buffer, offset + nameOff), nameLen / 2) ?? "";
            if (name.Length == 0)
                return null;
            return new Record { Name = name, Reason = reason, TimeStamp = timestamp };
        }
        catch
        {
            return null;
        }
    }

    public static string ReasonText(uint reason)
    {
        var parts = new List<string>(4);
        if ((reason & 0x1) != 0) parts.Add("Data overwrite");
        if ((reason & 0x2) != 0) parts.Add("Data extend");
        if ((reason & 0x4) != 0) parts.Add("Data truncation");
        if ((reason & 0x10) != 0) parts.Add("Named data overwrite");
        if ((reason & 0x20) != 0) parts.Add("Named data extend");
        if ((reason & 0x40) != 0) parts.Add("Named data truncation");
        if ((reason & 0x100) != 0) parts.Add("File create");
        if ((reason & 0x200) != 0) parts.Add("File delete");
        if ((reason & 0x400) != 0) parts.Add("Extended attribute change");
        if ((reason & 0x800) != 0) parts.Add("Security change");
        if ((reason & 0x1000) != 0) parts.Add("Rename: old name");
        if ((reason & 0x2000) != 0) parts.Add("Rename: new name");
        if ((reason & 0x4000) != 0) parts.Add("Indexable change");
        if ((reason & 0x8000) != 0) parts.Add("Basic info change");
        if ((reason & 0x10000) != 0) parts.Add("Hard link change");
        if ((reason & 0x20000) != 0) parts.Add("Compression change");
        if ((reason & 0x40000) != 0) parts.Add("Encryption change");
        if ((reason & 0x80000) != 0) parts.Add("Object ID change");
        if ((reason & 0x100000) != 0) parts.Add("Reparse point change");
        if ((reason & 0x200000) != 0) parts.Add("Stream change");
        if ((reason & 0x2000000) != 0) parts.Add("Transacted change");
        if ((reason & 0x4000000) != 0) parts.Add("Integrity change");
        if ((reason & 0x8000000) != 0) parts.Add("Storage class change");
        if ((reason & 0x80000000u) != 0) parts.Add("Close");
        uint known = 0x1u | 0x2u | 0x4u | 0x10u | 0x20u | 0x40u | 0x100u | 0x200u | 0x400u
            | 0x800u | 0x1000u | 0x2000u | 0x4000u | 0x8000u | 0x10000u | 0x20000u | 0x40000u
            | 0x80000u | 0x100000u | 0x200000u | 0x2000000u | 0x4000000u | 0x8000000u | 0x80000000u;
        uint unknown = reason & ~known;
        if (unknown != 0) parts.Add($"Flag 0x{unknown:X8}");
        return string.Join(" | ", parts);
    }
}
