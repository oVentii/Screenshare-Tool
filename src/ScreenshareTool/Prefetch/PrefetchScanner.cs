using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Serilog;

internal static class PrefetchVolumeMap
{
    private static readonly ILogger Logger = Log.ForContext(typeof(PrefetchVolumeMap));

    public sealed class Map
    {
        public Dictionary<string, string> SerialToLetter { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> GuidToLetter { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public static Map Build()
    {
        var map = new Map();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;
                string root = drive.RootDirectory.FullName;
                string letter = drive.Name.TrimEnd('\\');
                if (PrefetchNative.GetVolumeInformation(
                        root, IntPtr.Zero, 0,
                        out uint serial, out _, out _, IntPtr.Zero, 0))
                {
                    map.SerialToLetter[serial.ToString("X8")] = letter;
                }
                var name = new System.Text.StringBuilder(128);
                if (PrefetchNative.GetVolumeNameForMountPoint(root, name, (uint)name.Capacity))
                {
                    // Form: \\?\Volume{xxxxxxxx-...}\ — index by the inner GUID text.
                    string guid = name.ToString().Trim();
                    int open = guid.IndexOf('{');
                    int close = guid.IndexOf('}');
                    if (open >= 0 && close > open)
                        map.GuidToLetter[guid.Substring(open + 1, close - open - 1)] = letter;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Volume serial lookup failed for {Drive}", drive.Name);
            }
        }
        return map;
    }

    public static string ResolveVolumePath(string volumePath, Map map)
    {
        if (string.IsNullOrEmpty(volumePath))
            return volumePath;

        int start = volumePath.IndexOf("VOLUME{", StringComparison.Ordinal);
        if (start < 0)
            return volumePath;
        int end = volumePath.IndexOf('}', start);
        if (end < 0)
            return volumePath;

        string inner = volumePath.Substring(start + 7, end - start - 7);
        int dash = inner.IndexOf('-');
        if (dash >= 0)
        {
            string serial = inner.Substring(dash + 1).ToUpperInvariant();
            if (map.SerialToLetter.TryGetValue(serial, out string? letter))
                return letter + volumePath.Substring(end + 1);
        }
        // Real volume GUIDs (not serial-derived) fall through to the GUID map.
        if (map.GuidToLetter.TryGetValue(inner, out string? guidLetter))
            return guidLetter + volumePath.Substring(end + 1);
        return volumePath;
    }
}

internal static class PrefetchLogonSessions
{
    public static List<long> GetInteractiveLogons()
    {
        var result = new List<long>();
        try
        {
            uint status = PrefetchNative.EnumerateLogonSessions(out uint count, out IntPtr list);
            if (status != 0 || list == IntPtr.Zero)
                return result;

            try
            {
                for (uint i = 0; i < count; i++)
                {
                    IntPtr luid = IntPtr.Add(list, (int)(i * 8));
                    if (PrefetchNative.GetLogonSessionData(luid, out IntPtr data) != 0 || data == IntPtr.Zero)
                        continue;
                    try
                    {
                        int logonType = Marshal.ReadInt32(data, PrefetchNative.LogonTypeOffset);
                        if (logonType != PrefetchNative.InteractiveLogon &&
                            logonType != PrefetchNative.RemoteInteractiveLogon)
                            continue;
                        long fileTime = Marshal.ReadInt64(data, PrefetchNative.LogonTimeOffset);
                        if (fileTime == 0)
                            continue;
                        try
                        {
                            long unix = DateTime.FromFileTimeUtc(fileTime).Ticks / TimeSpan.TicksPerSecond
                                - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond;
                            result.Add(unix);
                        }
                        catch
                        {
                        }
                    }
                    finally
                    {
                        PrefetchNative.FreeReturnBuffer(data);
                    }
                }
            }
            finally
            {
                PrefetchNative.FreeReturnBuffer(list);
            }
        }
        catch
        {
        }
        return result;
    }
}

public static class PrefetchScanner
{
    private static readonly ILogger Logger = Log.ForContext(typeof(PrefetchScanner));

    private const int RelatedSendCap = 300;

    public static PrefetchResult Run(CancellationToken ct = default)
    {
        try
        {
            return RunAsync(ct).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return new PrefetchResult { Error = "Scan cancelled." };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Prefetch scan failed");
            return new PrefetchResult { Error = "Check failed: " + ex.Message };
        }
    }

    public static Task<PrefetchResult> RunAsync(CancellationToken ct = default)
        => RunCoreAsync(ct);

    private static async Task<PrefetchResult> RunCoreAsync(CancellationToken ct)
    {
        var result = new PrefetchResult
        {
            Admin = ForensicUtil.IsAdministrator(),
            PrefetchDir = GetPrefetchDir()
        };

        string[] files;
        try
        {
            if (!Directory.Exists(result.PrefetchDir))
            {
                result.Error = "Prefetch directory not found.";
                return result;
            }
            files = Directory.GetFiles(result.PrefetchDir, "*.pf");
        }
        catch (Exception ex)
        {
            result.Error = "Cannot list Prefetch directory: " + ex.Message;
            return result;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        result.FilesFound = files.Length;

        var volumeMap = PrefetchVolumeMap.Build();
        var logons = PrefetchLogonSessions.GetInteractiveLogons();
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string ownPath = Environment.ProcessPath ?? "";
        var sigCache = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        var entries = new PrefetchEntry?[files.Length];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(8, Math.Max(4, Environment.ProcessorCount)),
            CancellationToken = ct
        };

        await Task.Run(() => Parallel.For(0, files.Length, options, i =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                entries[i] = ScanOne(files[i], volumeMap, logons, nowUnix, ownPath, sigCache, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One bad file (locked, corrupt, native failure) must never
                // abort the whole scan; keep a conservative degraded entry.
                Logger.Warning(ex, "Prefetch entry failed for {File}", files[i]);
                entries[i] = new PrefetchEntry
                {
                    Filename = Path.GetFileName(files[i]),
                    IsSigned = false,
                    IsPresent = true,
                    MatchedRules = new List<string> { "none" }
                };
            }
        }), ct).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            if (entry is null)
                continue;
            result.Entries.Add(entry);
        }

        result.FindingCount = result.Entries.Count(e =>
            !e.IsSigned || e.MatchedRules.Any(r => !string.Equals(r, "none", StringComparison.Ordinal)));
        return result;
    }

    private static PrefetchEntry? ScanOne(
        string filePath,
        PrefetchVolumeMap.Map volumeMap,
        List<long> logons,
        long nowUnix,
        string ownPath,
        System.Collections.Concurrent.ConcurrentDictionary<string, bool> sigCache,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var parser = new PrefetchParser(filePath);
        if (!parser.Success)
            return null;

        var entry = new PrefetchEntry
        {
            Filename = Path.GetFileName(filePath),
            ExecutedUnix = parser.ExecutedTime(),
            LastRunsUnix = parser.LastEightExecutionTimes().ToList(),
            IsPresent = true,
            MatchedRules = new List<string>()
        };
        entry.ReadableTime = FormatUnix(entry.ExecutedUnix);
        entry.LastRunsExact = parser.LastRunTimesExact();
        entry.LastRunsExact = parser.LastRunTimesExact();
        try
        {
            var fi = new FileInfo(filePath);
            entry.PfSizeBytes = fi.Length;
            entry.PfCreated = fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
            entry.PfAccessed = fi.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss");
            entry.PfModified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
        }

        var related = parser.GetFilenamesStrings();
        entry.RelatedTotal = related.Count;

        // Stem-match over the FULL list (cheap) so the resolved binary is
        // found even when it sits past the send cap; only the first N rows
        // (with presence info) travel to the UI.
        var resolvedAll = new List<string>(related.Count);
        foreach (string name in related)
            resolvedAll.Add(PrefetchVolumeMap.ResolveVolumePath(name, volumeMap));

        string stem = entry.Filename;
        int hyphen = stem.IndexOf('-');
        if (hyphen > 0)
            stem = stem.Substring(0, hyphen);

        foreach (string candidate in resolvedAll)
        {
            if (candidate.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0 &&
                candidate.IndexOf('.') >= 0)
            {
                entry.ProperPath = candidate;
                break;
            }
        }

        int send = Math.Min(resolvedAll.Count, RelatedSendCap);
        for (int r = 0; r < send; r++)
        {
            bool present;
            try { present = File.Exists(resolvedAll[r]); }
            catch { present = false; }
            entry.RelatedFiles.Add(new PrefetchRelatedFile { Path = resolvedAll[r], Present = present });
        }

        entry.IsInInstance = IsInInstance(entry.ReadableTime, logons, nowUnix);

        if (!string.IsNullOrEmpty(entry.ProperPath))
        {
            ct.ThrowIfCancellationRequested();

            bool exists;
            try { exists = File.Exists(entry.ProperPath); }
            catch { exists = false; }

            if (!exists)
            {
                entry.IsSigned = false;
                entry.IsPresent = false;
                entry.MatchedRules.Add("none");
                return entry;
            }

            entry.IsSigned = GetCachedSignature(entry.ProperPath, sigCache);
            ct.ThrowIfCancellationRequested();
            if (!entry.IsSigned)
            {
                if (!string.IsNullOrEmpty(ownPath) &&
                    string.Equals(entry.ProperPath, ownPath, StringComparison.OrdinalIgnoreCase))
                {
                }
                else
                {
                    var hits = new List<PrefetchRules.RuleHit>();
                    if (PrefetchRules.ScanFile(entry.ProperPath, hits) && hits.Count > 0)
                    {
                        foreach (var hit in hits)
                        {
                            entry.MatchedRules.Add(hit.Id);
                            entry.MatchedDetails.Add(new PrefetchRuleHit
                            {
                                Id = hit.Id,
                                Name = hit.DisplayName,
                                Sample = hit.Sample,
                                Encoding = hit.Encoding,
                                Offset = hit.Offset,
                                Count = hit.Count,
                                Context = hit.Context
                            });
                        }
                    }
                    else
                        entry.MatchedRules.Add("none");
                }
            }
            else
            {
                entry.MatchedRules.Add("none");
            }
        }

        return entry;
    }

    private static bool GetCachedSignature(
        string path,
        System.Collections.Concurrent.ConcurrentDictionary<string, bool> cache)
    {
        if (cache.TryGetValue(path, out bool cached))
            return cached;
        bool result = PrefetchSignature.IsFileSignatureValid(path);
        cache.TryAdd(path, result);
        return result;
    }

    private const int RelatedSigCap = 200;

    public static List<PrefetchRelatedSig> CheckRelatedSignatures(
        List<string> paths, CancellationToken ct = default)
    {
        var distinct = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(RelatedSigCap)
            .ToList();

        var results = new PrefetchRelatedSig?[distinct.Count];
        var cache = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct
        };

        Parallel.For(0, distinct.Count, options, i =>
        {
            ct.ThrowIfCancellationRequested();
            bool signed = false;
            try
            {
                signed = File.Exists(distinct[i]) &&
                    GetCachedSignature(distinct[i], cache);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                signed = false;
            }
            results[i] = new PrefetchRelatedSig { Path = distinct[i], Signed = signed };
        });

        return results.Where(r => r is not null).Select(r => r!).ToList();
    }

    private static bool IsInInstance(
        string readableTime, List<long> logons, long nowUnix)
    {
        if (logons.Count == 0)
            return false;
        if (!DateTime.TryParseExact(readableTime, "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime local))
            return false;
        long execUnix = new DateTimeOffset(local).ToUnixTimeSeconds();
        long firstLogon = logons[0];
        return execUnix >= firstLogon && execUnix <= nowUnix;
    }

    private static string FormatUnix(long unix)
    {
        if (unix <= 0)
            return "";
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return "";
        }
    }

    private static string GetPrefetchDir()
    {
        try
        {
            string win = ForensicUtil.GetWindowsDirectory();
            if (!string.IsNullOrEmpty(win))
                return Path.Combine(win, "Prefetch");
        }
        catch
        {
        }
        return @"C:\Windows\Prefetch";
    }
}

public sealed class PrefetchResult
{
    [JsonPropertyName("admin")]
    public bool Admin { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    [JsonPropertyName("findingCount")]
    public int FindingCount { get; set; }
    [JsonPropertyName("prefetchDir")]
    public string PrefetchDir { get; set; } = "";
    [JsonPropertyName("filesFound")]
    public int FilesFound { get; set; }
    [JsonPropertyName("entries")]
    public List<PrefetchEntry> Entries { get; set; } = new();
}

public sealed class PrefetchEntry
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";
    [JsonPropertyName("properPath")]
    public string ProperPath { get; set; } = "";
    [JsonPropertyName("readableTime")]
    public string ReadableTime { get; set; } = "";
    [JsonPropertyName("executedUnix")]
    public long ExecutedUnix { get; set; }
    [JsonPropertyName("lastRunsUnix")]
    public List<long> LastRunsUnix { get; set; } = new();
    [JsonPropertyName("lastRunsExact")]
    public List<string> LastRunsExact { get; set; } = new();
    [JsonPropertyName("relatedFiles")]
    public List<PrefetchRelatedFile> RelatedFiles { get; set; } = new();
    [JsonPropertyName("relatedTotal")]
    public int RelatedTotal { get; set; }
    [JsonPropertyName("isSigned")]
    public bool IsSigned { get; set; }
    [JsonPropertyName("isPresent")]
    public bool IsPresent { get; set; }
    [JsonPropertyName("isInInstance")]
    public bool IsInInstance { get; set; }
    [JsonPropertyName("matchedRules")]
    public List<string> MatchedRules { get; set; } = new();
    [JsonPropertyName("matchedDetails")]
    public List<PrefetchRuleHit> MatchedDetails { get; set; } = new();
    [JsonPropertyName("pfSizeBytes")]
    public long PfSizeBytes { get; set; }
    [JsonPropertyName("pfCreated")]
    public string PfCreated { get; set; } = "";
    [JsonPropertyName("pfAccessed")]
    public string PfAccessed { get; set; } = "";
    [JsonPropertyName("pfModified")]
    public string PfModified { get; set; } = "";
}

public sealed class PrefetchRelatedFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    [JsonPropertyName("present")]
    public bool Present { get; set; }
}

public sealed class PrefetchRuleHit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("sample")]
    public string Sample { get; set; } = "";
    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = "";
    [JsonPropertyName("offset")]
    public long Offset { get; set; }
    [JsonPropertyName("count")]
    public int Count { get; set; }
    [JsonPropertyName("context")]
    public string Context { get; set; } = "";
}

public sealed class PrefetchRelatedSig
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    [JsonPropertyName("signed")]
    public bool Signed { get; set; }
}
