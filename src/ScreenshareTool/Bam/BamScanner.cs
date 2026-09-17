using System.Diagnostics;
using Serilog;













public static class BamScanner
{
    private static readonly ILogger Logger = Log.ForContext(typeof(BamScanner));

    public static async Task<BamScanResult> ScanAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new BamScanResult { RequiresAdmin = !ForensicUtil.IsAdministrator() };

        bool isAdmin = ForensicUtil.IsAdministrator();
        long logonUnix = ForensicUtil.GetLogonUnixTime();

        
        
        
        
        
        
        
        var scan = BamCoreParser.ParseLive();
        var entries = new List<BamEntryInfo>(scan.Artifacts.Count);

        foreach (var artifact in scan.Artifacts)
        {
            if (ct.IsCancellationRequested)
                return result;

            string path = artifact.Path.Display;
            string lower = ForensicUtil.NormalizePath(path);

            bool isSystem = IsSystemPath(lower);
            bool wantSignature = isAdmin &&
                                 !ForensicUtil.IsUnresolved(path) &&
                                 !string.IsNullOrEmpty(lower);

            var info = new BamEntryInfo
            {
                Path = path,
                LastExecution = artifact.LastExecutionUtc ?? "",
                LastExecutionUnix = artifact.LastExecutionUnix ?? 0,
                FileExists = wantSignature && ForensicUtil.FileExistsNative(path),
                IsSystemEntry = isSystem
            };

            if (wantSignature && info.FileExists)
            {
                var verdict = SignatureChecker.Evaluate(path);
                info.Signature = verdict.Status;
                info.SignatureDetail = verdict.Detail;

                
                
                if (verdict.Status is SignatureStatus.Unsigned or SignatureStatus.NotMZ)
                {
                    var rules = YaraScanner.Scan(path, checkPe: true);
                    info.MatchedRules = rules;
                    if (rules.Count > 0)
                    {
                        info.Signature = SignatureStatus.Cheat;
                        info.SignatureDetail = "YARA match: " + string.Join(", ", rules);
                    }
                }
            }
            else if (lower.Length > 0 && !info.FileExists)
            {
                info.Signature = SignatureStatus.NotFound;
                info.SignatureDetail = "File not found — signature not evaluated";
            }

            if (logonUnix > 0 && info.LastExecutionUnix >= logonUnix)
                info.InLogonWindow = true;

            entries.Add(info);
        }

        
        entries.Sort((a, b) => b.LastExecutionUnix.CompareTo(a.LastExecutionUnix));
        result.Entries = entries;

        
        result.Total = entries.Count;
        result.Unsigned = entries.Count(e => e.Signature == SignatureStatus.Unsigned);
        result.CheatSig = entries.Count(e => e.Signature == SignatureStatus.Cheat && e.MatchedRules.Count == 0);
        result.YaraMatch = entries.Count(e => e.MatchedRules.Count > 0);
        result.FakeSig = entries.Count(e => e.Signature == SignatureStatus.Fake);
        result.NotFound = entries.Count(e => e.Signature == SignatureStatus.NotFound);

        
        
        if (isAdmin)
        {
            var (deleted, error) = DeletedBamSystemHive.ReadDeletedPaths(ct);
            result.DeletedReadFailed = error is not null;
            result.DeletedReadError = error ?? "";
            foreach (string dp in deleted)
            {
                result.DeletedPaths.Add(new DeletedBamPath
                {
                    Path = dp,
                    LastExecution = ""
                });
            }
            result.DeletedCount = result.DeletedPaths.Count;

            result.DeniedEntries = BamRegistrySecurity.ScanDeniedPermissions();
            result.DeniedCount = result.DeniedEntries.Count;
        }

        sw.Stop();
        result.ScanSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
        return result;
    }

    
    private static long ParseUnix(string? stamp)
    {
        if (string.IsNullOrWhiteSpace(stamp))
            return 0;
        if (DateTimeOffset.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dto))
        {
            return dto.ToUnixTimeSeconds();
        }
        return 0;
    }

    
    
    
    
    
    
    internal static bool IsSystemPath(string lower,
        string? windowsDir = null,
        string? programData = null,
        string? programFiles = null)
    {
        if (lower.Length == 0)
            return true;

        static string Root(string? dir) => (dir ?? "").TrimEnd('\\') + "\\";

        string win = Root(windowsDir ?? ForensicUtil.GetWindowsDirectory()).ToLowerInvariant();
        if (win.Length > 1 && lower.StartsWith(win, StringComparison.Ordinal))
            return true;

        string pd = Root(programData ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
            .ToLowerInvariant();
        if (pd.Length > 1 && lower.StartsWith(pd + "microsoft\\", StringComparison.Ordinal))
            return true;

        string pf = Root(programFiles ?? ForensicUtil.GetProgramFiles()).ToLowerInvariant();
        if (pf.Length > 1 && lower.StartsWith(pf + "windowsapps\\", StringComparison.Ordinal))
            return true;

        return false;
    }
}
