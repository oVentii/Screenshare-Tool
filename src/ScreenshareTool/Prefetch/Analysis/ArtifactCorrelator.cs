using System.IO;
using Serilog;







internal static class ArtifactCorrelator
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ArtifactCorrelator));
    private const int MaxGhosts = 150;

    public static void Correlate(
        PrefetchScanResult result,
        PrefetchParser.RiskContext context,
        IReadOnlyList<AmcacheReader.AmcacheEntry> amcache,
        IReadOnlyList<ShimCacheReader.ShimCacheEntry> shim,
        IReadOnlyList<BamReader.BamEntry> bam)
    {
        try
        {
            CorrelateCore(result, context, amcache, shim, bam);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Artifact correlation failed");
        }
    }

    private static void CorrelateCore(
        PrefetchScanResult result,
        PrefetchParser.RiskContext context,
        IReadOnlyList<AmcacheReader.AmcacheEntry> amcache,
        IReadOnlyList<ShimCacheReader.ShimCacheEntry> shim,
        IReadOnlyList<BamReader.BamEntry> bam)
    {
        var covered = new ArtifactPathIndex();
        foreach (var e in result.Entries)
        {
            Cover(covered, e.MainExecutablePath);
            Cover(covered, e.LastSeenPath);
        }
        foreach (var e in result.RecoveredDeleted)
        {
            Cover(covered, e.MainExecutablePath);
            Cover(covered, e.LastSeenPath);
        }

        var bag = new Dictionary<string, GhostDraft>(StringComparer.Ordinal);

        void Touch(string path, string? publisher, bool inShim, bool inAm, bool inBam, string? timestamp)
        {
            if (string.IsNullOrWhiteSpace(path) || ForensicUtil.IsUnresolved(path))
                return;
            string dos = VolumeMapper.ToDosPath(path);
            string key = ForensicUtil.NormalizePath(dos);
            if (key.Length == 0)
                return;
            if (!bag.TryGetValue(key, out var draft))
            {
                bag[key] = draft = new GhostDraft { Path = dos };
            }
            if (inShim) draft.InShim = true;
            if (inAm) draft.InAm = true;
            if (inBam) draft.InBam = true;
            if (!string.IsNullOrEmpty(publisher))
                draft.Publisher = publisher;
            if (!string.IsNullOrEmpty(timestamp) &&
                (string.IsNullOrEmpty(draft.LastSeenTime) || inAm))
            {
                draft.LastSeenTime = timestamp;
                draft.LastSeenSource = inAm ? "Amcache" : inBam ? "BAM" : "ShimCache";
            }
        }

        foreach (var e in shim)
            Touch(e.Path, null, inShim: true, inAm: false, inBam: false, e.Modified);
        foreach (var e in amcache)
            Touch(e.Path, e.Publisher, inShim: false, inAm: true, inBam: false, e.LinkDate);
        foreach (var e in bam)
            Touch(e.Path, null, inShim: false, inAm: false, inBam: true, e.LastExecution);

        var ghosts = new List<GhostArtifact>(32);

        foreach (var draft in bag.Values)
        {
            if (ghosts.Count >= MaxGhosts)
                break;
            if (covered.Contains(draft.Path))
                continue;

            string fileName = Path.GetFileName(draft.Path);
            if (IsTrustedOsFileName(fileName) && !PrefetchParser.IsSuspiciousExecutablePath(draft.Path))
                continue;

            bool missing = !ForensicUtil.FileExistsNative(draft.Path);
            bool suspiciousPath = PrefetchParser.IsSuspiciousExecutablePath(draft.Path);
            bool mcPath = PrefetchParser.IsMinecraftRelatedPath(draft.Path);
            bool interestingName = LooksCheatFileName(fileName);

            if (!missing && !suspiciousPath && !mcPath && !interestingName)
                continue;

            var sig = missing ? SignatureStatus.NotFound : SignatureChecker.GetSignatureStatus(draft.Path);
            if (!missing && sig == SignatureStatus.Signed && !interestingName && !mcPath)
                continue;

            var yara = new List<string>();
            if (!missing && sig is not SignatureStatus.Signed)
            {
                try
                {
                    
                    
                    
                    var fi = new FileInfo(ForensicUtil.ResolveExistingPath(draft.Path));
                    if (fi.Exists && fi.Length > 0)
                        yara = YaraScanner.Scan(fi.FullName, checkPe: true);
                }
                catch
                {
                    
                }
            }

            int score = 20;
            if (missing) score += 18;
            if (draft.InShim) score += 8;
            if (draft.InAm) score += 8;
            if (draft.InBam) score += 8;
            if (sig is SignatureStatus.Fake or SignatureStatus.Cheat) score += 30;
            else if (sig is SignatureStatus.Unsigned or SignatureStatus.NotMZ) score += 12;
            if (yara.Count > 0) score += 20;
            if (mcPath && sig is not SignatureStatus.Signed) score += 10;
            if (score > 100) score = 100;

            var sources = new List<string>(3);
            if (draft.InShim) sources.Add("ShimCache");
            if (draft.InAm) sources.Add("Amcache");
            if (draft.InBam) sources.Add("BAM");
            string sourceLabel = string.Join("+", sources);

            var ghost = new GhostArtifact
            {
                Path = draft.Path,
                FileName = fileName,
                InShimCache = draft.InShim,
                InAmcache = draft.InAm,
                InBam = draft.InBam,
                FileMissing = missing,
                PrefetchMissing = true,
                SignatureStatus = sig,
                MatchedRules = yara,
                Publisher = draft.Publisher,
                Sources = sourceLabel,
                RiskScore = score,
                LastSeenTime = draft.LastSeenTime,
                LastSeenPath = draft.Path
            };
            ghosts.Add(ghost);
        }

        result.GhostArtifacts = ghosts
            .OrderByDescending(g => g.RiskScore)
            .ThenBy(g => g.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        result.GhostCount = result.GhostArtifacts.Count;

        var ghostCards = new List<PrefetchInfo>(result.GhostArtifacts.Count);
        foreach (var g in result.GhostArtifacts)
        {
            var info = new PrefetchInfo
            {
                PfFileName = (g.FileName.Length > 0 ? g.FileName : "unknown.exe") + " (no .pf)",
                PfPath = "(leftover — " + g.Sources + ")",
                PresenceKind = PresenceKind.GhostLeftover,
                FileMissing = g.FileMissing,
                ArtifactLeftover = true,
                WasDeleted = false,
                MainExecutablePath = g.Path,
                LastSeenPath = g.LastSeenPath ?? g.Path,
                LastSeenSource = g.Sources,
                LastSeenTime = g.LastSeenTime,
                AmcachePublisher = g.Publisher,
                MainSignatureStatus = g.SignatureStatus,
                MatchedRules = g.MatchedRules,
                InShimCache = g.InShimCache,
                InAmcache = g.InAmcache,
                InBam = g.InBam,
                RunCount = 0,
                ResolvedFrom = g.Sources
            };
            info.SignatureDetail = g.FileMissing
                ? PrefetchParser.PresenceDetail(info)
                : SignatureChecker.Evaluate(g.Path).Detail;
            PrefetchParser.RecomputeRisk(info, context);
            ghostCards.Add(info);
        }
        result.GhostEntries = ghostCards;

        CollectIntegrity(result);
    }

    public static void BuildSignals(PrefetchScanResult result)
    {
        var signals = new List<string>(8);
        if (result.DeletedCount > 0)
            signals.Add(result.DeletedCount + " deleted Prefetch recovered from USN");
        if (result.UsnJournalDisabled)
            signals.Add("USN journal disabled this session");
        if (result.UsnJournalRecreated)
            signals.Add("USN journal recreated after boot");
        if (result.EventLogCleared)
            signals.Add("Event log cleared this session (" +
                        string.Join(", ", result.EventLogClears.Select(c => c.Label)) + ")");
        if (result.GhostCount > 0)
            signals.Add(result.GhostCount + " leftover executions in ShimCache/Amcache/BAM with no Prefetch");
        if (result.EvidenceGap)
            signals.Add("Prefetch present but ShimCache and Amcache both empty");
        if (result.FakeSig > 0)
            signals.Add(result.FakeSig + " fake/untrusted signatures");
        if (result.TimestompCount > 0)
            signals.Add(result.TimestompCount + " possible timestomped PEs");
        if (result.LeftoverCount > 0)
            signals.Add(result.LeftoverCount + " Prefetch leftovers (cache still lists a missing file)");
        result.CorrelationSignals = signals;
    }

    private static void Cover(ArtifactPathIndex index, string? path)
    {
        if (!string.IsNullOrEmpty(path))
            index.Add(path);
    }

    private static void CollectIntegrity(PrefetchScanResult result)
    {
        try
        {
            DateTime boot = ForensicUtil.GetBootTimeLocal();
            var clears = ForensicIntegrity.DetectEventLogClears(boot);
            result.EventLogClears = clears.Select(c => new EventLogClearHit
            {
                Label = c.Label,
                Channel = c.Channel,
                EventId = c.EventId,
                When = c.When
            }).ToList();
            result.EventLogCleared = result.EventLogClears.Count > 0;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Event-log clear detection failed");
        }
    }

    private static bool IsTrustedOsFileName(string fileName)
    {
        string exe = Path.GetFileNameWithoutExtension(fileName);
        return exe.Length > 0 && TrustedOsNames.Contains(exe);
    }

    private static bool LooksCheatFileName(string fileName)
    {
        string n = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        string[] names =
        {
            "inject", "loader", "mapper", "kdmapper", "cheat", "aimbot", "hack",
            "macro", "bypass", "unhook", "spoofer", "triggerbot", "wallhack",
            "clicker", "autoclick", "vape", "slinky","lunar","feather","meteor","wurst","rusher","entropy","xynox","nullsense"
        };
        return names.Any(x => n.Contains(x, StringComparison.Ordinal));
    }

    private static readonly HashSet<string> TrustedOsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSMAIN", "SUPERFETCH", "SVCHOST", "RUNTIMEBROKER", "SEARCHINDEXER",
        "MSMPENG", "NISSRV", "SECURITYHEALTHSERVICE", "SMARTSCREEN", "CONSENT",
        "TIWORKER", "TRUSTEDINSTALLER", "WUAUCLT", "DLLHOST", "TASKHOSTW",
        "SIHOST", "CTFMON", "FONDRVHOST", "SANDBOXBROKER", "BACKGROUNDTASKHOST",
        "APPLICATIONFRAMEHOST", "SHELLEXPERIENCEHOST", "STARTMENUEXPERIENCEHOST",
        "SEARCHAPP", "CONHOST", "CSRSS", "WININIT", "SERVICES", "LSASS", "SMSS",
        "EXPLORER", "NOTEPAD", "CMD", "POWERSHELL", "PWSH", "MMC", "TASKMGR",
        "SPOOLSV", "WINLOGON", "USERINIT", "DWMINIT", "DWM", "FONTSUB",
        "SEARCHPROTOCOLHOST", "SEARCHFILTERHOST", "MOZILLAMAINTENANCE"
    };

    private sealed class GhostDraft
    {
        public string Path { get; init; } = "";
        public string? Publisher { get; set; }
        public string? LastSeenTime { get; set; }
        public string? LastSeenSource { get; set; }
        public bool InShim { get; set; }
        public bool InAm { get; set; }
        public bool InBam { get; set; }
    }
}
