using System.IO;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;
using Serilog;
using System.Text.Json.Serialization;

public static class ServiceChecker
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ServiceChecker));
    private static readonly TimeSpan BootGrace = TimeSpan.FromMinutes(2);
    private const int RecycleItemCap = 15_000;
    private const int ScmEventScanLimit = 1_200;

    private static readonly (string[] Names, string FallbackDisplay, bool Critical)[] WatchedServices =
    {
        (new[] { "SysMain", "Superfetch" }, "SysMain / Superfetch", true),
        (new[] { "PcaSvc" }, "Program Compatibility Assistant Service", true),
        (new[] { "DPS" }, "Diagnostic Policy Service", true),
        (new[] { "EventLog" }, "Windows Event Log", true),
        (new[] { "Schedule" }, "Task Scheduler", true),
        (new[] { "Bam" }, "Background Activity Moderator", true),
        (new[] { "DusmSvc" }, "Data Usage", false),
        (new[] { "Appinfo" }, "Application Information", true),
        (new[] { "CDPSvc" }, "Connected Devices Platform Service", false),
        (new[] { "DcomLaunch" }, "DCOM Server Process Launcher", true),
        (new[] { "PlugPlay" }, "Plug and Play", true),
        (new[] { "WSearch" }, "Windows Search", false)
    };

    public static Task<ServiceCheckResult> RunAsync(CancellationToken ct = default)
        => RunCoreAsync(ct);

    private static async Task<ServiceCheckResult> RunCoreAsync(CancellationToken ct)
    {
        var result = new ServiceCheckResult
        {
            Admin = ForensicUtil.IsAdministrator()
        };
        DateTime bootLocal = ForensicUtil.GetBootTimeLocal();
        result.Boot = CollectBoot(bootLocal);

        Task<List<ServiceDriveInfo>> drivesTask = Task.Run(() => CollectDrives(ct), ct);
        Task<List<ServiceEntry>> servicesTask = Task.Run(() => CollectServices(ct), ct);
        await Task.WhenAll(drivesTask, servicesTask).ConfigureAwait(false);
        result.Drives = drivesTask.Result;
        result.Services = servicesTask.Result;

        if (result.Admin)
        {
            Task<ServiceEventsInfo> eventsTask = Task.Run(() => CollectEvents(bootLocal, ct), ct);
            Task<RecycleBinInfo> recycleTask = Task.Run(() => CollectRecycleBin(bootLocal, ct), ct);
            await Task.WhenAll(eventsTask, recycleTask).ConfigureAwait(false);
            result.Events = eventsTask.Result;
            result.Recycle = recycleTask.Result;
        }
        else
        {
            result.Events = new ServiceEventsInfo { Skipped = true };
            result.Recycle = new RecycleBinInfo { Skipped = true };
        }

        result.FindingCount = CountFindings(result);
        return result;
    }

    private static int CountFindings(ServiceCheckResult result)
    {
        int n = 0;
        if (result.Boot.TickMismatch)
            n++;
        n += result.Services.Count(s => s.Severity == "bad");
        if (result.Events is { Skipped: false } ev)
        {
            n += ev.Clears.Count(c => c.Detected);
            n += ev.Usn.Count(u => u.Severity == "bad");
            if (ev.UnexpectedShutdown) n++;
            if (ev.ClockChanged) n++;
            if (ev.EventLogRestarted) n++;
        }
        return n;
    }

    private static ServiceBootInfo CollectBoot(DateTime bootLocal)
    {
        var info = new ServiceBootInfo
        {
            LastBoot = bootLocal.ToString("yyyy-MM-dd HH:mm:ss")
        };

        try
        {
            TimeSpan uptime = DateTime.Now - bootLocal;
            if (uptime < TimeSpan.Zero)
                uptime = TimeSpan.Zero;
            info.Uptime = FormatUptime(uptime);

            DateTime tickBoot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
            double diffMin = Math.Abs((tickBoot - bootLocal).TotalMinutes);
            if (diffMin > 5)
            {
                info.TickMismatch = true;
                info.TickBootEstimate = tickBoot.ToString("yyyy-MM-dd HH:mm:ss");
                info.TickNote = "Differs from registry \u2014 sleep or clock change";
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Boot time collection failed");
            info.LastBoot = info.LastBoot.Length > 0 ? info.LastBoot : "Unavailable";
        }

        return info;
    }

    private static string FormatUptime(TimeSpan up)
        => $"{(int)up.TotalHours}h {up.Minutes}m {up.Seconds}s";

    private static List<ServiceDriveInfo> CollectDrives(CancellationToken ct)
    {
        var list = new List<ServiceDriveInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!drive.IsReady || drive.DriveType == DriveType.CDRom)
                    continue;

                string root = drive.Name.TrimEnd('\\');
                string fs = string.IsNullOrEmpty(drive.DriveFormat) ? "Unknown" : drive.DriveFormat;
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "" : drive.VolumeLabel;
                string media = drive.DriveType == DriveType.Fixed ? GetMediaType(root) : "";
                string type = DescribeDriveType(drive.DriveType, media);

                list.Add(new ServiceDriveInfo
                {
                    Letter = root,
                    FileSystem = fs,
                    Media = type,
                    Label = label,
                    Size = FormatDriveSize(drive)
                });
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Skipped unreadable drive {Name}", drive.Name);
            }
        }
        return list;
    }

    private static string DescribeDriveType(DriveType type, string media)
    {
        return type switch
        {
            DriveType.Fixed => media == "Unknown" ? "Fixed" : media,
            DriveType.Removable => "Removable",
            DriveType.Network => "Network",
            DriveType.Ram => "RAM Disk",
            _ => "Unknown"
        };
    }

    private static string FormatDriveSize(DriveInfo drive)
    {
        try
        {
            long total = drive.TotalSize;
            if (total <= 0) return "";
            long free = drive.TotalFreeSpace;
            return FormatBytes(free) + " free of " + FormatBytes(total);
        }
        catch
        {
            return "";
        }
    }

    private static readonly uint[] MediaAccessModes = { NativeMethods.GENERIC_READ, 0 };

    private static string GetMediaType(string root)
    {
        foreach (uint access in MediaAccessModes)
        {
            IntPtr h = NativeMethods.CreateFileW(
                @"\\.\" + root,
                access,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);

            if (h == NativeMethods.InvalidHandleValue)
                continue;

            try
            {
                byte[] query = new byte[12];
                BitConverter.GetBytes(StorageDeviceSeekPenaltyProperty).CopyTo(query, 0);
                BitConverter.GetBytes((uint)0).CopyTo(query, 4);

                byte[] output = new byte[12];
                if (NativeMethods.DeviceIoControl(
                    h, IOCTL_STORAGE_QUERY_PROPERTY, query, (uint)query.Length,
                    output, (uint)output.Length, out _, IntPtr.Zero))
                {
                    bool incurs = output[8] != 0;
                    return incurs ? "HDD" : "SSD";
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Media type query failed for {Root}", root);
            }
            finally
            {
                NativeMethods.CloseHandle(h);
            }
        }

        return "Unknown";
    }

    private static List<ServiceEntry> CollectServices(CancellationToken ct)
    {
        var entries = new List<ServiceEntry>(WatchedServices.Length);

        foreach (var (names, fallbackDisplay, critical) in WatchedServices)
        {
            ct.ThrowIfCancellationRequested();
            var entry = new ServiceEntry
            {
                Name = names[0],
                Display = fallbackDisplay,
                Critical = critical
            };

            if (!TryOpenService(names, out ServiceController? service, out string resolvedName, out bool denied) ||
                service is null)
            {
                entry.Status = denied ? "Denied" : "Missing";
                entry.Severity = critical ? "bad" : "warn";
                entries.Add(entry);
                continue;
            }

            using (service)
            {
                entry.Name = resolvedName;
                try { entry.Display = service.DisplayName; }
                catch { entry.Display = fallbackDisplay; }

                try { entry.StartType = DescribeStartType(service, resolvedName); }
                catch { entry.StartType = "Unknown"; }

                ServiceControllerStatus status;
                try { status = service.Status; }
                catch
                {
                    entry.Status = "Denied";
                    entry.Severity = "bad";
                    entries.Add(entry);
                    continue;
                }

                entry.Status = DescribeStatus(status);
                if (status == ServiceControllerStatus.Running)
                {
                    int pid = ForensicUtil.GetServicePid(resolvedName, out bool shared);
                    if (pid <= 0 && !string.Equals(resolvedName, names[0], StringComparison.OrdinalIgnoreCase))
                        pid = ForensicUtil.GetServicePid(names[0], out shared);
                    entry.Pid = pid > 0 ? pid : null;
                    entry.SharedHost = shared;
                    if (pid > 0)
                    {
                        try
                        {
                            using var proc = Process.GetProcessById(pid);
                            entry.StartedAt = proc.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                        catch
                        {
                        }
                    }
                }

                entry.Severity = ClassifyService(entry, status, critical);
            }

            entries.Add(entry);
        }

        FillMissingStartTimes(entries, ct);
        return entries;
    }

    private static string DescribeStatus(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => "Running",
        ServiceControllerStatus.Stopped => "Stopped",
        ServiceControllerStatus.Paused => "Paused",
        ServiceControllerStatus.StartPending => "Start pending",
        ServiceControllerStatus.StopPending => "Stop pending",
        ServiceControllerStatus.ContinuePending => "Continue pending",
        ServiceControllerStatus.PausePending => "Pause pending",
        _ => status.ToString()
    };

    private static string DescribeStartType(ServiceController service, string serviceName)
    {
        ServiceStartMode mode;
        try { mode = service.StartType; }
        catch { return "Unknown"; }

        if (mode == ServiceStartMode.Automatic && IsDelayedAutoStart(serviceName))
            return "Automatic (Delayed)";

        return mode switch
        {
            ServiceStartMode.Automatic => "Automatic",
            ServiceStartMode.Manual => "Manual",
            ServiceStartMode.Disabled => "Disabled",
            ServiceStartMode.Boot => "Boot",
            ServiceStartMode.System => "System",
            _ => mode.ToString()
        };
    }

    private static bool IsDelayedAutoStart(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + serviceName);
            object? value = key?.GetValue("DelayedAutostart");
            return value is int i && i != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ClassifyService(ServiceEntry entry, ServiceControllerStatus status, bool critical)
    {
        if (status == ServiceControllerStatus.Running)
            return "ok";
        if (status is ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending)
            return "warn";
        if (status == ServiceControllerStatus.Paused)
            return "warn";

        bool automatic = entry.StartType.StartsWith("Automatic", StringComparison.OrdinalIgnoreCase);
        bool disabled = string.Equals(entry.StartType, "Disabled", StringComparison.OrdinalIgnoreCase);

        if (critical && (disabled || automatic || status == ServiceControllerStatus.Stopped))
            return "bad";
        if (disabled)
            return "warn";
        return "warn";
    }

    private static bool TryOpenService(string[] names, out ServiceController? service, out string resolvedName, out bool denied)
    {
        denied = false;
        foreach (string name in names)
        {
            ServiceController? sc = null;
            try
            {
                sc = new ServiceController(name);
                _ = sc.Status;
                service = sc;
                resolvedName = name;
                return true;
            }
            catch (InvalidOperationException)
            {
                sc?.Dispose();
            }
            catch (OperationCanceledException) { sc?.Dispose(); throw; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is UnauthorizedAccessException)
            {
                sc?.Dispose();
                denied = true;
                Logger.Debug(ex, "Access denied opening service {Name}", name);
            }
            catch (Exception ex)
            {
                sc?.Dispose();
                Logger.Debug(ex, "Could not open service {Name}", name);
            }
        }

        service = null;
        resolvedName = names[0];
        return false;
    }

    private static void FillMissingStartTimes(List<ServiceEntry> entries, CancellationToken ct)
    {
        var missing = entries
            .Where(e => string.Equals(e.Status, "Running", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrEmpty(e.StartedAt))
            .ToList();
        if (missing.Count == 0)
            return;

        ApplyScmTimes(missing, ct);
    }

    private static void ApplyScmTimes(
        List<ServiceEntry> missing, CancellationToken ct)
    {
        try
        {
            var q = new EventLogQuery("System", PathType.LogName, "*[System[(EventID=7036)]]")
            {
                ReverseDirection = true
            };
            using var reader = new EventLogReader(q);

            int scanned = 0;
            int remaining = missing.Count;
            while (remaining > 0 && scanned++ < ScmEventScanLimit)
            {
                ct.ThrowIfCancellationRequested();
                using var evt = reader.ReadEvent();
                if (evt is null) break;
                if (!string.Equals(evt.ProviderName, "Service Control Manager", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (evt.Properties.Count < 2 || !evt.TimeCreated.HasValue)
                    continue;

                string? svc = evt.Properties[0].Value?.ToString()?.Trim();
                string? extra = evt.Properties[1].Value?.ToString()?.Trim();
                if (string.IsNullOrEmpty(svc))
                    continue;

                if (IsStoppedState(extra))
                    continue;

                foreach (var entry in missing)
                {
                    if (!string.IsNullOrEmpty(entry.StartedAt))
                        continue;
                    if (!MatchesServiceName(svc, entry.Name, entry.Display))
                        continue;
                    entry.StartedAt = evt.TimeCreated.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    remaining--;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "SCM event 7036 lookup failed");
        }
    }

    private static bool IsStoppedState(string? state)
    {
        if (string.IsNullOrEmpty(state)) return false;
        return state.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0
            || state.IndexOf("paus", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesServiceName(string? value, string serviceName, string displayName)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        if (string.Equals(value, serviceName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(serviceName, "SysMain", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(value, "Superfetch", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(serviceName, "Superfetch", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(value, "SysMain", StringComparison.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrEmpty(displayName) &&
               string.Equals(value, displayName, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceEventsInfo CollectEvents(DateTime bootLocal, CancellationToken ct)
    {
        var info = new ServiceEventsInfo();
        DateTime timeThreshold = bootLocal + BootGrace;

        var clears = ForensicIntegrity.DetectEventLogClears(bootLocal);
        info.Clears = WatchedClearLabels.Select(label =>
        {
            var hit = clears.FirstOrDefault(c => string.Equals(c.Label, label, StringComparison.Ordinal));
            return new ServiceSignal
            {
                Key = label,
                Detected = hit is not null,
                When = hit?.When,
                Severity = hit is not null ? "bad" : "ok"
            };
        }).ToList();

        ct.ThrowIfCancellationRequested();
        info.Usn = CollectUsn(ct);

        DateTime? shutdown = ForensicIntegrity.GetLatestEventTime("System", 1074, provider: null, after: null);
        info.LastShutdown = shutdown?.ToString("yyyy-MM-dd HH:mm:ss");

        DateTime? unexpected = ForensicIntegrity.GetLatestEventTime("System", 6008, provider: null, after: bootLocal);
        info.UnexpectedShutdown = unexpected.HasValue;
        info.UnexpectedShutdownAt = unexpected?.ToString("yyyy-MM-dd HH:mm:ss");

        DateTime? timeChange = ForensicIntegrity.GetLatestEventTime(
            "Security", 4616, "Microsoft-Windows-Security-Auditing", timeThreshold);
        info.ClockChanged = timeChange.HasValue;
        info.ClockChangedAt = timeChange?.ToString("yyyy-MM-dd HH:mm:ss");

        DateTime? logStart = ForensicIntegrity.GetLatestEventTime("System", 6005, "EventLog", null)
                             ?? ForensicIntegrity.GetLatestEventTime("System", 6005, "Microsoft-Windows-Eventlog", null);
        if (logStart.HasValue)
        {
            info.EventLogStartAt = logStart.Value.ToString("yyyy-MM-dd HH:mm:ss");
            if (logStart.Value >= timeThreshold)
            {
                info.EventLogRestarted = true;
                info.EventLogStartNote = "Restarted this session";
            }
            else
            {
                info.EventLogStartNote = "Boot";
            }
        }

        DateTime? device = ForensicIntegrity.GetLatestEventTime("Microsoft-Windows-Kernel-PnP/Configuration", 400, null, bootLocal)
                           ?? ForensicIntegrity.GetLatestEventTime("System", 400, null, bootLocal)
                           ?? ForensicIntegrity.GetLatestEventTime("System", 225, null, bootLocal);
        info.LastDeviceChange = device?.ToString("yyyy-MM-dd HH:mm:ss");

        return info;
    }

    private static readonly string[] WatchedClearLabels =
    {
        "Security log (1102)",
        "System log (104)",
        "Application log (104)",
        "Setup log (104)"
    };

    private static List<ServiceUsnInfo> CollectUsn(CancellationToken ct)
    {
        var list = new List<ServiceUsnInfo>();
        var reader = new UsnJournalReader();
        bool anyNtfs = false;

        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.Network)
                    continue;
                if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                    continue;

                anyNtfs = true;
                char letter = char.ToUpperInvariant(drive.Name[0]);
                var status = reader.CheckIntegrity(letter, BootGrace);
                var item = new ServiceUsnInfo { Volume = letter.ToString() };

                if (status.JournalDisabled)
                {
                    item.State = "Disabled this session";
                    item.Severity = "bad";
                }
                else if (!status.JournalEnabled)
                {
                    item.State = status.Error ?? "Inaccessible";
                    item.Severity = "warn";
                }
                else if (status.JournalRecreated &&
                         DateTime.TryParseExact(status.JournalCreationTime,
                             "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                             DateTimeStyles.None, out var created))
                {
                    item.State = "Recreated this session";
                    item.When = created.ToString("yyyy-MM-dd HH:mm:ss");
                    item.Severity = "bad";
                }
                else
                {
                    item.State = "Active";
                    item.Severity = "ok";
                }

                list.Add(item);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "USN check failed for {Drive}", drive.Name);
            }
        }

        if (!anyNtfs)
        {
            list.Add(new ServiceUsnInfo
            {
                Volume = "",
                State = "No NTFS volumes available",
                Severity = "warn"
            });
        }

        return list;
    }

    private static RecycleBinInfo CollectRecycleBin(DateTime bootLocal, CancellationToken ct)
    {
        var info = new RecycleBinInfo();
        var newest = default((DateTime When, string Path)?);
        var oldest = default((DateTime When, string Path)?);

        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.Network)
                    continue;

                string recycleBinPath = Path.Combine(drive.Name, "$Recycle.Bin");
                if (!Directory.Exists(recycleBinPath))
                    continue;

                info.Volumes++;
                ScanRecycleBin(recycleBinPath, bootLocal, info, ref newest, ref oldest, ct);
            }
            catch
            {
                info.Inaccessible++;
            }
        }

        if (newest.HasValue)
        {
            info.NewestAt = newest.Value.When.ToString("yyyy-MM-dd HH:mm:ss");
            info.NewestPath = newest.Value.Path;
        }
        if (oldest.HasValue)
        {
            info.OldestAt = oldest.Value.When.ToString("yyyy-MM-dd HH:mm:ss");
            info.OldestPath = oldest.Value.Path;
        }

        info.TotalSizeLabel = FormatBytes(info.TotalSize);
        return info;
    }

    private static void ScanRecycleBin(
        string recycleBinPath,
        DateTime bootLocal,
        RecycleBinInfo info,
        ref (DateTime When, string Path)? newest,
        ref (DateTime When, string Path)? oldest,
        CancellationToken ct)
    {
        foreach (var sidDir in new DirectoryInfo(recycleBinPath)
            .EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var meta in sidDir.EnumerateFiles("$I*", SearchOption.TopDirectoryOnly))
                {
                    if (info.Items >= RecycleItemCap)
                        return;

                    var item = ParseRecycleBinMetadata(meta.FullName);
                    if (item is null)
                    {
                        info.Inaccessible++;
                        continue;
                    }

                    info.Items++;
                    info.TotalSize += item.Size;
                    if (item.DeletedAt >= bootLocal)
                        info.DeletedThisSession++;

                    if (!oldest.HasValue || item.DeletedAt < oldest.Value.When)
                        oldest = (item.DeletedAt, item.OriginalPath);
                    if (!newest.HasValue || item.DeletedAt > newest.Value.When)
                        newest = (item.DeletedAt, item.OriginalPath);
                }
            }
            catch
            {
                info.Inaccessible++;
            }
        }
    }

    private sealed record RecycleItem(string OriginalPath, long Size, DateTime DeletedAt);

    private static RecycleItem? ParseRecycleBinMetadata(string metaPath)
    {
        try
        {
            byte[] data;
            using (var fs = new FileStream(metaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length < 24 || fs.Length > 65_536)
                    return null;
                data = new byte[(int)fs.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = fs.Read(data, read, data.Length - read);
                    if (n == 0) return null;
                    read += n;
                }
            }

            long size = BitConverter.ToInt64(data, 8);
            long fileTime = BitConverter.ToInt64(data, 16);
            if (fileTime <= 0)
                return null;

            DateTime deleted;
            try { deleted = DateTime.FromFileTimeUtc(fileTime).ToLocalTime(); }
            catch { return null; }

            string path = ReadRecycleBinPath(data);
            if (string.IsNullOrEmpty(path))
                path = Path.GetFileNameWithoutExtension(metaPath);

            return new RecycleItem(path, size < 0 ? 0 : size, deleted);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadRecycleBinPath(byte[] data)
    {
        if (data.Length < 24)
            return "";

        long version = BitConverter.ToInt64(data, 0);
        bool win10 = version == 2 || (version != 1 && data.Length >= 28);
        if (win10)
        {
            int pathLen = BitConverter.ToInt32(data, 24);
            int maxChars = (data.Length - 28) / 2;
            if (pathLen > 0 && pathLen <= maxChars)
                return Encoding.Unicode.GetString(data, 28, pathLen * 2).TrimEnd('\0');
        }

        int start = (version == 2 && data.Length >= 28) ? 28 : 24;
        var sb = new StringBuilder();
        for (int i = start; i + 1 < data.Length; i += 2)
        {
            char c = (char)((data[i + 1] << 8) | data[i]);
            if (c == '\0') break;
            if (c < 0x20 && c != '\t') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB" };

    private static string FormatBytes(long bytes)
    {
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < ByteUnits.Length - 1)
        {
            value /= 1024;
            i++;
        }
        string num = i == 0 ? value.ToString("0") : value.ToString("0.0");
        return num + " " + ByteUnits[i];
    }

    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const uint StorageDeviceSeekPenaltyProperty = 7;
}
public sealed class ServiceCheckResult
{
    [JsonPropertyName("admin")]
    public bool Admin { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    [JsonPropertyName("findingCount")]
    public int FindingCount { get; set; }
    [JsonPropertyName("boot")]
    public ServiceBootInfo Boot { get; set; } = new();
    [JsonPropertyName("drives")]
    public List<ServiceDriveInfo> Drives { get; set; } = new();
    [JsonPropertyName("services")]
    public List<ServiceEntry> Services { get; set; } = new();
    [JsonPropertyName("events")]
    public ServiceEventsInfo? Events { get; set; }
    [JsonPropertyName("recycle")]
    public RecycleBinInfo? Recycle { get; set; }
}
public sealed class ServiceBootInfo
{
    [JsonPropertyName("lastBoot")]
    public string LastBoot { get; set; } = "";
    [JsonPropertyName("uptime")]
    public string Uptime { get; set; } = "";
    [JsonPropertyName("tickMismatch")]
    public bool TickMismatch { get; set; }
    [JsonPropertyName("tickBootEstimate")]
    public string? TickBootEstimate { get; set; }
    [JsonPropertyName("tickNote")]
    public string? TickNote { get; set; }
}
public sealed class ServiceDriveInfo
{
    [JsonPropertyName("letter")]
    public string Letter { get; set; } = "";
    [JsonPropertyName("fileSystem")]
    public string FileSystem { get; set; } = "";
    [JsonPropertyName("media")]
    public string Media { get; set; } = "";
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";
    [JsonPropertyName("size")]
    public string Size { get; set; } = "";
}
public sealed class ServiceEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("display")]
    public string Display { get; set; } = "";
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
    [JsonPropertyName("startType")]
    public string StartType { get; set; } = "";
    [JsonPropertyName("startedAt")]
    public string? StartedAt { get; set; }
    [JsonPropertyName("pid")]
    public int? Pid { get; set; }
    [JsonPropertyName("sharedHost")]
    public bool SharedHost { get; set; }
    [JsonPropertyName("critical")]
    public bool Critical { get; set; }
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "ok";
}
public sealed class ServiceEventsInfo
{
    [JsonPropertyName("skipped")]
    public bool Skipped { get; set; }
    [JsonPropertyName("clears")]
    public List<ServiceSignal> Clears { get; set; } = new();
    [JsonPropertyName("usn")]
    public List<ServiceUsnInfo> Usn { get; set; } = new();
    [JsonPropertyName("lastShutdown")]
    public string? LastShutdown { get; set; }
    [JsonPropertyName("unexpectedShutdown")]
    public bool UnexpectedShutdown { get; set; }
    [JsonPropertyName("unexpectedShutdownAt")]
    public string? UnexpectedShutdownAt { get; set; }
    [JsonPropertyName("clockChanged")]
    public bool ClockChanged { get; set; }
    [JsonPropertyName("clockChangedAt")]
    public string? ClockChangedAt { get; set; }
    [JsonPropertyName("eventLogRestarted")]
    public bool EventLogRestarted { get; set; }
    [JsonPropertyName("eventLogStartAt")]
    public string? EventLogStartAt { get; set; }
    [JsonPropertyName("eventLogStartNote")]
    public string? EventLogStartNote { get; set; }
    [JsonPropertyName("lastDeviceChange")]
    public string? LastDeviceChange { get; set; }
}
public sealed class ServiceSignal
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";
    [JsonPropertyName("detected")]
    public bool Detected { get; set; }
    [JsonPropertyName("when")]
    public string? When { get; set; }
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "ok";
}
public sealed class ServiceUsnInfo
{
    [JsonPropertyName("volume")]
    public string Volume { get; set; } = "";
    [JsonPropertyName("state")]
    public string State { get; set; } = "";
    [JsonPropertyName("when")]
    public string? When { get; set; }
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "ok";
}
public sealed class RecycleBinInfo
{
    [JsonPropertyName("skipped")]
    public bool Skipped { get; set; }
    [JsonPropertyName("volumes")]
    public int Volumes { get; set; }
    [JsonPropertyName("items")]
    public int Items { get; set; }
    [JsonPropertyName("totalSize")]
    public long TotalSize { get; set; }
    [JsonPropertyName("totalSizeLabel")]
    public string TotalSizeLabel { get; set; } = "0 B";
    [JsonPropertyName("deletedThisSession")]
    public int DeletedThisSession { get; set; }
    [JsonPropertyName("inaccessible")]
    public int Inaccessible { get; set; }
    [JsonPropertyName("newestAt")]
    public string? NewestAt { get; set; }
    [JsonPropertyName("newestPath")]
    public string? NewestPath { get; set; }
    [JsonPropertyName("oldestAt")]
    public string? OldestAt { get; set; }
    [JsonPropertyName("oldestPath")]
    public string? OldestPath { get; set; }
}
