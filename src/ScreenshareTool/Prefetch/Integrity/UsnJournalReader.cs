using System.IO;
using System.Text;
using System.Text.Json.Serialization;























public sealed class UsnJournalReader
{
    public sealed class UsnEvent
    {
        [JsonPropertyName("oldName")]
        public string? OldName { get; set; }
        [JsonPropertyName("newName")]
        public string? NewName { get; set; }
        [JsonPropertyName("action")]
        public string? Action { get; set; }
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }
        [JsonPropertyName("isPrefetchDir")]
        public bool IsPrefetchDir { get; set; }
        [JsonPropertyName("fileReferenceNumber")]
        public ulong FileReferenceNumber { get; set; }
        [JsonPropertyName("parentFileReferenceNumber")]
        public ulong ParentFileReferenceNumber { get; set; }
    }

    private const int BufferSize = 16 * 1024 * 1024;

    
    public sealed class UsnJournalStatus
    {
        [JsonPropertyName("journalEnabled")]
        public bool JournalEnabled { get; set; }
        [JsonPropertyName("journalFileExists")]
        public bool JournalFileExists { get; set; }
        [JsonPropertyName("journalRecreated")]
        public bool JournalRecreated { get; set; }
        [JsonPropertyName("journalDisabled")]
        public bool JournalDisabled { get; set; }
        [JsonPropertyName("journalCreationTime")]
        public string? JournalCreationTime { get; set; }
        [JsonPropertyName("bootTime")]
        public string? BootTime { get; set; }
        [JsonPropertyName("statusDetail")]
        public string? StatusDetail { get; set; }
        [JsonPropertyName("journalId")]
        public ulong JournalId { get; set; }
        [JsonPropertyName("firstUsn")]
        public long FirstUsn { get; set; }
        [JsonPropertyName("nextUsn")]
        public long NextUsn { get; set; }
        [JsonPropertyName("lowestValidUsn")]
        public long LowestValidUsn { get; set; }
        [JsonPropertyName("maxUsn")]
        public long MaxUsn { get; set; }
        [JsonPropertyName("maximumSize")]
        public ulong MaximumSize { get; set; }
        [JsonPropertyName("usnRecordCount")]
        public long UsnRecordCount { get; set; }
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    
    
    
    
    
    
    
    
    
    
    public UsnJournalStatus CheckIntegrity(char driveLetter)
        => CheckIntegrity(driveLetter, bootGrace: TimeSpan.FromMinutes(2));

    public UsnJournalStatus CheckIntegrity(char driveLetter, TimeSpan bootGrace)
    {
        var st = new UsnJournalStatus();
        DateTime bootUtc = ForensicUtil.GetBootTimeUtc();
        st.BootTime = bootUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        try
        {
            IntPtr handle = NativeMethods.CreateFileW(
                $"\\\\.\\{driveLetter}:", NativeMethods.GENERIC_READ,
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
                    st.StatusDetail = "USN journal $J created after boot — journal was deleted and rebuilt";
                }
                else if (st.JournalEnabled && createdUtc > bootUtc && createdUtc < threshold)
                {
                    st.StatusDetail = "USN journal $J created near boot (OS init) — not treated as a wipe";
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
    public sealed class UsnScanResult
    {
        [JsonPropertyName("events")]
        public List<UsnEvent> Events { get; set; } = new();
        [JsonPropertyName("error")]
        public string? Error { get; set; }
        [JsonPropertyName("truncated")]
        public bool Truncated { get; set; }
    }

    
    
    
    
    public IReadOnlyList<UsnEvent> Run(char driveLetter)
        => RunDetailed(driveLetter, startUsn: null, sinceUnixTime: null).Events;

    
    
    
    
    public UsnScanResult RunDetailed(char driveLetter)
        => RunDetailed(driveLetter, startUsn: null, sinceUnixTime: null);

    public IReadOnlyList<UsnEvent> Run(char driveLetter, long? startUsn, long? sinceUnixTime)
        => RunDetailed(driveLetter, startUsn, sinceUnixTime).Events;

    public UsnScanResult RunDetailed(char driveLetter, long? startUsn, long? sinceUnixTime)
    {
        var result = new UsnScanResult();
        try
        {
            Scan(driveLetter, startUsn, sinceUnixTime, result);
        }
        catch (Exception ex)
        {
            result.Error ??= ex.Message;
        }
        return result;
    }

    private void Scan(char driveLetter, long? startUsn, long? sinceUnixTime, UsnScanResult result)
    {
        _renameCache.Clear();
        _trackedPrefetchFrn.Clear();
        _trackedPfFrn.Clear();

        IntPtr handle = NativeMethods.CreateFileW(
            $"\\\\.\\{driveLetter}:", NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == NativeMethods.InvalidHandleValue)
        {
            result.Error = "Cannot open volume (error " + NativeMethods.GetLastError()
                + "). Restart as Administrator to read the USN journal.";
            return;
        }

        try
        {
            if (!QueryJournal(handle, out var journal, out int queryError))
            {
                if (queryError == NativeMethods.ERROR_JOURNAL_NOT_ACTIVE)
                    result.Error = "USN journal is not active (deleted or disabled).";
                else if (queryError == NativeMethods.ERROR_JOURNAL_DELETE_IN_PROGRESS)
                    result.Error = "USN journal delete is in progress.";
                else
                    result.Error = "Journal query failed (error " + queryError + ").";
                return;
            }

            long bootUnix = new DateTimeOffset(ForensicUtil.GetBootTimeUtc()).ToUnixTimeSeconds();
            long minTime = sinceUnixTime ?? bootUnix;
            byte[] buffer = new byte[BufferSize];
            ProbePrefetchDirectoryFrns(driveLetter);

            const long maxUsnSpan = 64L * 1024 * 1024;
            const int maxIoctl = 24;
            const int maxEvents = 8000;
            long start = startUsn ?? journal.LowestValidUsn;
            if (journal.NextUsn > start && journal.NextUsn - start > maxUsnSpan)
                start = Math.Max(journal.LowestValidUsn, journal.NextUsn - maxUsnSpan);

            
            
            
            var readV1 = new NativeMethods.READ_USN_JOURNAL_DATA_V1
            {
                StartUsn = start,
                ReasonMask = BuildReasonMask(),
                ReturnOnlyOnClose = 0,
                Timeout = 0,
                BytesToWaitFor = 0,
                UsnJournalID = journal.UsnJournalID,
                MinMajorVersion = 2,
                MaxMajorVersion = 2
            };

            bool v1 = DeviceIoControl(handle, NativeMethods.FSCTL_READ_USN_JOURNAL,
                ref readV1, buffer, out uint firstReturned);
            int loops = 0;
            if (v1)
            {
                if (firstReturned > sizeof(long))
                {
                    ParseUsnBuffer(buffer, (int)firstReturned, minTime, result.Events);
                    readV1.StartUsn = BitConverter.ToInt64(buffer, 0);
                    loops++;
                }
                while (loops < maxIoctl && result.Events.Count < maxEvents &&
                       DeviceIoControl(handle, NativeMethods.FSCTL_READ_USN_JOURNAL,
                           ref readV1, buffer, out uint bytesReturned) &&
                       bytesReturned > sizeof(long))
                {
                    ParseUsnBuffer(buffer, (int)bytesReturned, minTime, result.Events);
                    readV1.StartUsn = BitConverter.ToInt64(buffer, 0);
                    loops++;
                }
            }
            else
            {
                var readV0 = new NativeMethods.READ_USN_JOURNAL_DATA_V0
                {
                    StartUsn = start,
                    ReasonMask = BuildReasonMask(),
                    ReturnOnlyOnClose = 0,
                    Timeout = 0,
                    BytesToWaitFor = 0,
                    UsnJournalID = journal.UsnJournalID
                };
                while (loops < maxIoctl && result.Events.Count < maxEvents &&
                       DeviceIoControl(handle, NativeMethods.FSCTL_READ_USN_JOURNAL,
                           ref readV0, buffer, out uint bytesReturned) &&
                       bytesReturned > sizeof(long))
                {
                    ParseUsnBuffer(buffer, (int)bytesReturned, minTime, result.Events);
                    readV0.StartUsn = BitConverter.ToInt64(buffer, 0);
                    loops++;
                }
            }

            if (result.Events.Count >= maxEvents || loops >= maxIoctl)
                result.Truncated = true;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private void ProcessRecord(byte[] buf, int p, int recordLength, long minTime, List<UsnEvent> results)
    {
        ushort major = BitConverter.ToUInt16(buf, p + 4);

        ulong frn, parentFrn;
        long filetime;
        uint reason;
        int nameOffset;
        int nameLen;

        if (major == 3)
        {
            
            if (recordLength < 76) return;
            frn = BitConverter.ToUInt64(buf, p + 8);
            parentFrn = BitConverter.ToUInt64(buf, p + 24);
            filetime = BitConverter.ToInt64(buf, p + 48);
            reason = BitConverter.ToUInt32(buf, p + 56);
            nameLen = BitConverter.ToUInt16(buf, p + 72);
            nameOffset = BitConverter.ToUInt16(buf, p + 74);
        }
        else if (major >= 4)
        {
            
            return;
        }
        else
        {
            if (recordLength < 60) return;
            frn = BitConverter.ToUInt64(buf, p + 8);
            parentFrn = BitConverter.ToUInt64(buf, p + 16);
            filetime = BitConverter.ToInt64(buf, p + 32);
            reason = BitConverter.ToUInt32(buf, p + 40);
            nameLen = BitConverter.ToUInt16(buf, p + 56);
            nameOffset = BitConverter.ToUInt16(buf, p + 58);
        }

        
        
        
        
        if (nameOffset >= recordLength) return;
        int nameBytes = Math.Min(nameLen, recordLength - nameOffset);
        string filename = Encoding.Unicode.GetString(buf, p + nameOffset, nameBytes);
        if (IsExactPrefetchDir(filename))
            _trackedPrefetchFrn.Add(frn);

        
        bool isDirItself = _trackedPrefetchFrn.Contains(frn);

        
        
        
        bool isPfName = filename.EndsWith(".pf", StringComparison.OrdinalIgnoreCase);
        bool underPrefetch = _prefetchDirFrn != 0 && parentFrn == _prefetchDirFrn;
        bool isPfFile = (underPrefetch && isPfName) || _trackedPfFrn.Contains(frn);
        if (_prefetchDirFrn == 0 && !isDirItself)
            isPfFile = isPfName || _trackedPfFrn.Contains(frn);
        if (isPfName && (underPrefetch || _prefetchDirFrn == 0) && frn != 0)
            _trackedPfFrn.Add(frn);

        long time = filetime == 0 ? 0 : FileTimeToUnixTime((ulong)filetime);
        bool renameOldEarly = (reason & NativeMethods.USN_REASON_RENAME_OLD_NAME) != 0;
        if (time != 0 && time <= minTime)
        {
            if (renameOldEarly && (isPfFile || isPfName || underPrefetch) && frn != 0)
            {
                _renameCache[frn] = (filename, time, isPfFile || isPfName);
                if (isPfName)
                    _trackedPfFrn.Add(frn);
            }
            return;
        }

        bool renameOld = (reason & NativeMethods.USN_REASON_RENAME_OLD_NAME) != 0;
        bool renameNew = (reason & NativeMethods.USN_REASON_RENAME_NEW_NAME) != 0;
        bool create = (reason & NativeMethods.USN_REASON_FILE_CREATE) != 0;
        bool delete = (reason & NativeMethods.USN_REASON_FILE_DELETE) != 0;
        bool secChange = (reason & NativeMethods.USN_REASON_SECURITY_CHANGE) != 0;
        bool hardLink = (reason & NativeMethods.USN_REASON_HARD_LINK_CHANGE) != 0;

        bool trackedRename = _renameCache.ContainsKey(frn) || _trackedPfFrn.Contains(frn);
        if (!isDirItself && !isPfFile && !trackedRename && !(renameOld && underPrefetch))
            return;

        if (renameOld && renameNew)
        {
            
            results.Add(NewEvent(filename, filename, "Renamed", time, isDirItself, frn, parentFrn,
                ReasonName(reason)));
        }
        else if (renameOld)
        {
            _renameCache[frn] = (filename, time, isPfFile || isPfName);
            if (isPfName && frn != 0)
                _trackedPfFrn.Add(frn);
        }
        else if (renameNew)
        {
            if (_renameCache.Remove(frn, out var prev))
            {
                results.Add(NewEvent(prev.oldName, filename, "Renamed", time, isDirItself, frn, parentFrn,
                    ReasonName(reason)));
            }
            else
            {
                
                
                results.Add(NewEvent("", filename, "Renamed (new name)", time, isDirItself, frn, parentFrn,
                    ReasonName(reason)));
            }
        }
        else if (delete)
        {
            results.Add(NewEvent(filename, "", isDirItself ? "Prefetch Directory Delete" : "Deleted",
                time, isDirItself, frn, parentFrn, ReasonName(reason)));
        }
        else if (isDirItself && create)
        {
            results.Add(NewEvent(filename, "", "Prefetch Directory Create",
                time, isDirItself, frn, parentFrn, ReasonName(reason)));
        }
        else if (secChange)
        {
            results.Add(NewEvent(filename, "", isDirItself ? "Prefetch Directory Security Change" : "Security Change",
                time, isDirItself, frn, parentFrn, ReasonName(reason)));
        }
        else if (hardLink)
        {
            results.Add(NewEvent(filename, "", isDirItself ? "Prefetch Directory Hard Link" : "Hard Link Created",
                time, isDirItself, frn, parentFrn, ReasonName(reason)));
        }
        
        
    }

    private static string ReasonName(uint reason)
    {
        var parts = new List<string>(8);
        if ((reason & NativeMethods.USN_REASON_DATA_OVERWRITE) != 0) parts.Add("DataOverwrite");
        if ((reason & NativeMethods.USN_REASON_DATA_EXTEND) != 0) parts.Add("DataExtend");
        if ((reason & NativeMethods.USN_REASON_NAMED_DATA_OVERWRITE) != 0) parts.Add("NamedDataOverwrite");
        if ((reason & NativeMethods.USN_REASON_NAMED_DATA_EXTEND) != 0) parts.Add("NamedDataExtend");
        if ((reason & NativeMethods.USN_REASON_FILE_CREATE) != 0) parts.Add("Create");
        if ((reason & NativeMethods.USN_REASON_FILE_DELETE) != 0) parts.Add("Delete");
        if ((reason & NativeMethods.USN_REASON_SECURITY_CHANGE) != 0) parts.Add("Security");
        if ((reason & NativeMethods.USN_REASON_RENAME_OLD_NAME) != 0) parts.Add("RenameOld");
        if ((reason & NativeMethods.USN_REASON_RENAME_NEW_NAME) != 0) parts.Add("RenameNew");
        if ((reason & NativeMethods.USN_REASON_HARD_LINK_CHANGE) != 0) parts.Add("HardLink");
        if ((reason & NativeMethods.USN_REASON_COMPRESSION_CHANGE) != 0) parts.Add("Compression");
        if ((reason & NativeMethods.USN_REASON_ENCRYPTION_CHANGE) != 0) parts.Add("Encryption");
        if ((reason & NativeMethods.USN_REASON_INDEXABLE_CHANGE) != 0) parts.Add("Indexable");
        if ((reason & NativeMethods.USN_REASON_CLOSE) != 0) parts.Add("Close");
        return parts.Count == 0 ? $"0x{reason:X8}" : string.Join(" | ", parts);
    }

    private static uint BuildReasonMask()
        => NativeMethods.USN_REASON_FILE_CREATE |
           NativeMethods.USN_REASON_FILE_DELETE |
           NativeMethods.USN_REASON_SECURITY_CHANGE |
           NativeMethods.USN_REASON_RENAME_OLD_NAME |
           NativeMethods.USN_REASON_RENAME_NEW_NAME |
           NativeMethods.USN_REASON_HARD_LINK_CHANGE;

    private void ParseUsnBuffer(byte[] buffer, int bytesReturned, long minTime, List<UsnEvent> results)
    {
        int pos = sizeof(long);
        int end = bytesReturned;
        while (pos + 60 <= end)
        {
            uint recordLength = BitConverter.ToUInt32(buffer, pos);
            if (recordLength == 0 || recordLength < 60) break;
            if (pos + (int)recordLength > end) break;
            ProcessRecord(buffer, pos, (int)recordLength, minTime, results);
            pos += (int)recordLength;
        }
    }

    private static UsnEvent NewEvent(string oldName, string newName, string action, long unixTime,
        bool isDir, ulong frn, ulong parentFrn, string reason)
        => new()
        {
            OldName = oldName,
            NewName = newName,
            Action = action,
            Reason = reason,
            Timestamp = unixTime > 0 ? ForensicUtil.UnixTimeToLocalString(unixTime) : "",
            IsPrefetchDir = isDir,
            FileReferenceNumber = frn,
            ParentFileReferenceNumber = parentFrn
        };

    private static bool IsExactPrefetchDir(string name)
        => string.Equals(name.Trim(), "prefetch", StringComparison.OrdinalIgnoreCase);

    

    private ulong _prefetchDirFrn;
    private readonly HashSet<ulong> _trackedPrefetchFrn = new();
    private readonly HashSet<ulong> _trackedPfFrn = new();
    private readonly Dictionary<ulong, (string oldName, long time, bool isPf)> _renameCache = new();

    
    
    
    
    
    private void ProbePrefetchDirectoryFrns(char driveLetter)
    {
        string prefetch = driveLetter == ForensicUtil.GetWindowsDriveLetter()
            ? ForensicUtil.GetPrefetchDirectory()
            : $"{driveLetter}:\\Windows\\Prefetch";
        IntPtr h = NativeMethods.CreateFileW(prefetch, 0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h == NativeMethods.InvalidHandleValue) return;
        try
        {
            var info = new byte[24];
            if (NativeMethods.GetFileInformationByHandleEx(h, NativeMethods.FileIdInfo, info, (uint)info.Length))
            {
                
                
                ulong frn = BitConverter.ToUInt64(info, 8);
                if (frn != 0)
                {
                    _prefetchDirFrn = frn;
                    _trackedPrefetchFrn.Add(frn);
                }
            }
        }
        finally
        {
            NativeMethods.CloseHandle(h);
        }
    }

    private static bool QueryJournal(IntPtr handle, out NativeMethods.USN_JOURNAL_DATA_V0 journal, out int error)
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

    private static bool DeviceIoControl(IntPtr handle, uint code,
        ref NativeMethods.READ_USN_JOURNAL_DATA_V1 inData, byte[] outBuf, out uint returned)
    {
        byte[] inBuf = ToBytes(inData);
        return NativeMethods.DeviceIoControl(handle, code, inBuf, (uint)inBuf.Length, outBuf, (uint)outBuf.Length, out returned, IntPtr.Zero);
    }

    private static bool DeviceIoControl(IntPtr handle, uint code,
        ref NativeMethods.READ_USN_JOURNAL_DATA_V0 inData, byte[] outBuf, out uint returned)
    {
        byte[] inBuf = ToBytes(inData);
        return NativeMethods.DeviceIoControl(handle, code, inBuf, (uint)inBuf.Length, outBuf, (uint)outBuf.Length, out returned, IntPtr.Zero);
    }

    private static byte[] ToBytes<T>(T value) where T : struct
    {
        var size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
        byte[] buf = new byte[size];
        var ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
        try
        {
            System.Runtime.InteropServices.Marshal.StructureToPtr(value, ptr, false);
            System.Runtime.InteropServices.Marshal.Copy(ptr, buf, 0, size);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
        }
        return buf;
    }

    private static long FileTimeToUnixTime(ulong filetime)
        => ForensicUtil.FileTimeToUnixTime(filetime);
}
