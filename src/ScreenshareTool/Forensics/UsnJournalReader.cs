using System.IO;

public sealed class UsnJournalReader
{
    public sealed class UsnJournalStatus
    {
        public bool JournalEnabled { get; set; }
        public bool JournalFileExists { get; set; }
        public bool JournalRecreated { get; set; }
        public bool JournalDisabled { get; set; }
        public string? JournalCreationTime { get; set; }
        public string? BootTime { get; set; }
        public string? StatusDetail { get; set; }
        public ulong JournalId { get; set; }
        public long FirstUsn { get; set; }
        public long NextUsn { get; set; }
        public long LowestValidUsn { get; set; }
        public long MaxUsn { get; set; }
        public ulong MaximumSize { get; set; }
        public long UsnRecordCount { get; set; }
        public string? Error { get; set; }
    }

    public UsnJournalStatus CheckIntegrity(char driveLetter, TimeSpan bootGrace)
    {
        var st = new UsnJournalStatus();
        DateTime bootUtc = ForensicUtil.GetBootTimeUtc();
        st.BootTime = bootUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        try
        {
            IntPtr handle = NativeMethods.CreateFileW(
                $@"\\.\{driveLetter}:", NativeMethods.GENERIC_READ,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == NativeMethods.InvalidHandleValue)
            {
                st.Error = "Cannot open volume (error " + NativeMethods.GetLastError() + ")";
                return st;
            }

            try
            {
                if (QueryJournal(handle, out var journal, out int queryError))
                {
                    st.JournalEnabled = true;
                    st.JournalId = journal.UsnJournalID;
                    st.FirstUsn = journal.FirstUsn;
                    st.NextUsn = journal.NextUsn;
                    st.LowestValidUsn = journal.LowestValidUsn;
                    st.MaxUsn = journal.MaxUsn;
                    st.MaximumSize = journal.MaximumSize;
                    st.UsnRecordCount = Math.Max(0, journal.NextUsn - journal.LowestValidUsn);
                }
                else
                {
                    st.JournalEnabled = false;
                    if (queryError == NativeMethods.ERROR_JOURNAL_NOT_ACTIVE)
                    {
                        st.JournalDisabled = true;
                        st.StatusDetail = "USN journal is not active (deleted or disabled with deletejournal /d)";
                    }
                    else if (queryError == NativeMethods.ERROR_JOURNAL_DELETE_IN_PROGRESS)
                    {
                        st.JournalDisabled = true;
                        st.StatusDetail = "USN journal delete is in progress";
                    }
                    else
                    {
                        st.Error = "Journal query failed (error " + queryError + ")";
                    }
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }

            if (TryGetJournalCreationUtc(driveLetter, out DateTime createdUtc))
            {
                st.JournalFileExists = true;
                st.JournalCreationTime = createdUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

                DateTime threshold = bootUtc + bootGrace;
                if (st.JournalEnabled && createdUtc >= threshold)
                {
                    st.JournalRecreated = true;
                    st.StatusDetail = "USN journal $J created after boot - journal was deleted and rebuilt";
                }
                else if (st.JournalEnabled && createdUtc > bootUtc && createdUtc < threshold)
                {
                    st.StatusDetail = "USN journal $J created near boot (OS init) - not treated as a wipe";
                }
            }
            else
            {
                st.JournalFileExists = false;
                if (st.JournalEnabled)
                    st.StatusDetail ??= "Journal is active but $J creation time is inaccessible";
            }
        }
        catch (Exception ex)
        {
            st.Error = ex.Message;
        }
        return st;
    }

    private static bool QueryJournal(
        IntPtr handle, out NativeMethods.USN_JOURNAL_DATA_V0 journal, out int error)
    {
        journal = default;
        byte[] outBuf = new byte[128];
        bool ok = NativeMethods.DeviceIoControl(
            handle, NativeMethods.FSCTL_QUERY_USN_JOURNAL,
            null, 0, outBuf, (uint)outBuf.Length, out uint returned, IntPtr.Zero);
        error = NativeMethods.GetLastError();
        if (!ok || returned < 56)
            return false;

        journal.UsnJournalID = BitConverter.ToUInt64(outBuf, 0);
        journal.FirstUsn = BitConverter.ToInt64(outBuf, 8);
        journal.NextUsn = BitConverter.ToInt64(outBuf, 16);
        journal.LowestValidUsn = BitConverter.ToInt64(outBuf, 24);
        journal.MaxUsn = BitConverter.ToInt64(outBuf, 32);
        journal.MaximumSize = BitConverter.ToUInt64(outBuf, 40);
        journal.AllocationDelta = BitConverter.ToUInt64(outBuf, 48);
        return true;
    }

    private static bool TryGetJournalCreationUtc(char driveLetter, out DateTime createdUtc)
    {
        createdUtc = default;
        char letter = char.ToUpperInvariant(driveLetter);
        string[] paths =
        {
            $@"\\?\{letter}:\$Extend\$UsnJrnl:$J",
            $@"{letter}:\$Extend\$UsnJrnl:$J"
        };
        foreach (string path in paths)
        {
            try
            {
                DateTime t = File.GetCreationTimeUtc(path);
                if (t.Year is >= 2000 and < 2100)
                {
                    createdUtc = t;
                    return true;
                }
            }
            catch
            {
            }
        }
        return false;
    }
}
