using System.Diagnostics;
using System.IO;
using Serilog;














internal static class PrefetchRecovery
{
    private static readonly ILogger Logger = Log.ForContext(typeof(PrefetchRecovery));

    private const int MaxFullRecoveries = 40;
    private static readonly TimeSpan RecoveryTimeBudget = TimeSpan.FromSeconds(25);

    public static List<PrefetchInfo> RecoverDeletedPf(string? rootPath,
        IReadOnlyList<PrefetchInfo> liveEntries, PrefetchParser.RiskContext? context,
        CancellationToken ct = default)
    {
        var result = new List<PrefetchInfo>();
        if (string.IsNullOrEmpty(rootPath) || liveEntries is null)
            return result;

        char drive = rootPath.TrimEnd('\\', '/').FirstOrDefault();
        if (drive == '\0')
            return result;
        drive = char.ToUpperInvariant(drive);

        var liveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var liveByExe = new Dictionary<string, PrefetchInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in liveEntries)
        {
            if (string.IsNullOrEmpty(e?.PfFileName))
                continue;
            liveNames.Add(e.PfFileName);
            string exe = PrefetchParser.ExtractExeNameFromPfFilename(e.PfFileName);
            if (exe.Length > 0 && !liveByExe.ContainsKey(exe))
                liveByExe[exe] = e;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            
            
            var scan = new UsnJournalReader().RunDetailed(drive);
            if (ct.IsCancellationRequested)
                return result;

            var events = scan.Events;
            if (events.Count == 0)
                return result;

            var deleted = events
                .Where(ev => !ev.IsPrefetchDir &&
                             ev.OldName != null &&
                             ev.OldName.EndsWith(".pf", StringComparison.OrdinalIgnoreCase))
                .GroupBy(ev => ev.OldName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First());

            int fullRecoveries = 0;
            foreach (var ev in deleted)
            {
                if (ct.IsCancellationRequested)
                    break;
                if (sw.Elapsed > RecoveryTimeBudget)
                    break;

                string oldName = ev.OldName ?? "";
                if (oldName.Length == 0 || liveNames.Contains(oldName))
                    continue;

                string exeName = PrefetchParser.ExtractExeNameFromPfFilename(oldName);
                if (exeName.Length == 0 || exeName[0] == '$')
                    continue;

                PrefetchInfo? info = null;

                
                
                
                if (ev.FileReferenceNumber != 0 && fullRecoveries < MaxFullRecoveries)
                {
                    byte[]? bytes = FileById.ReadDeletedBytes(drive, ev.FileReferenceNumber);
                    if (bytes is not null)
                    {
                        info = PrefetchParser.ParseData(bytes, oldName,
                            default, context, checkSignatures: true, wasDeleted: true);
                        if (info is not null)
                        {
                            info.PfPath = "(deleted — fully recovered from NTFS by file reference)";
                            fullRecoveries++;
                        }
                    }
                }

                if (info is null)
                {
                    string usnTime = ev.Timestamp ?? "";
                    info = new PrefetchInfo
                    {
                        PfFileName = oldName,
                        PfPath = "(deleted — recovered from USN journal)",
                        WasDeleted = true,
                        PresenceKind = PresenceKind.DeletedPrefetch,
                        RunCount = 1,
                        MainExecutablePath = ForensicUtil.UnresolvedPath,
                        MainSignatureStatus = SignatureStatus.NotFound,
                        LastSeenSource = "USN",
                        LastSeenTime = string.IsNullOrEmpty(usnTime) ? null : usnTime,
                        LastExecutionTimes = string.IsNullOrEmpty(usnTime)
                            ? new List<string>()
                            : new List<string> { usnTime }
                    };

                    if (liveByExe.TryGetValue(exeName, out var twin) &&
                        !ForensicUtil.IsUnresolved(twin.MainExecutablePath))
                    {
                        info.MainExecutablePath = twin.MainExecutablePath;
                    }

                    PrefetchParser.ScoreRecovered(info, context);
                    if (string.IsNullOrEmpty(info.LastSeenTime) && usnTime.Length > 0)
                        info.LastSeenTime = usnTime;
                    if (string.IsNullOrEmpty(info.LastSeenSource))
                        info.LastSeenSource = "USN";
                    info.SignatureDetail = PrefetchParser.PresenceDetail(info);
                }

                result.Add(info);
            }
        }
        catch (OperationCanceledException)
        {
            
            throw;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "USN recovery failed");
        }

        return result
            .OrderByDescending(p => p.RiskScore)
            .ThenBy(p => p.PfFileName)
            .ToList();
    }
}
