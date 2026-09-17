using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;




























internal static class PrefetchParser
{
    private const int MinPfSize = 0x100;
    internal const int MaxCompressedPfBytes = 8 * 1024 * 1024;
    private const int MaxReferencedFiles = 4096;

    private static readonly ConcurrentDictionary<string, List<string>> YaraCache =
        new(StringComparer.OrdinalIgnoreCase);
    private const int YaraCacheLimit = 2000;
    private const int FileStatCacheLimit = 30000;

    
    
    
    
    
    
    
    public sealed class RiskContext
    {
        public long LogonTime { get; set; }
        public ArtifactPathIndex ShimIndex { get; } = new();
        public ArtifactPathIndex AmcacheIndex { get; } = new();
        public ArtifactPathIndex BamIndex { get; } = new();
        public int ShimCacheCount { get; set; }
        public int AmcacheCount { get; set; }
        public int BamCount { get; set; }
        public bool ShimLoaded { get; set; }
        public bool AmcacheLoaded { get; set; }
        public bool BamLoaded { get; set; }
        
        public Task AmcacheLoadDone => _amcacheLoadDone.Task;
        private readonly TaskCompletionSource<bool> _amcacheLoadDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        
        public void SignalAmcacheLoadDone() => _amcacheLoadDone.TrySetResult(true);
        public List<ShimCacheReader.ShimCacheEntry> ShimEntries { get; set; } = new();
        public List<AmcacheReader.AmcacheEntry> AmcacheEntries { get; set; } = new();
        public List<BamReader.BamEntry> BamEntries { get; set; } = new();
        
        
        private readonly ConcurrentDictionary<string, List<PathResolver.CacheHint>> _cacheByName =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _scannedYara = new(StringComparer.OrdinalIgnoreCase);

        
        public bool TryReserveYara(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            lock (_scannedYara)
                return _scannedYara.Add(path);
        }

        public void IndexCacheHint(PathResolver.CacheHint hint)
        {
            if (string.IsNullOrWhiteSpace(hint.Path))
                return;
            string name = Path.GetFileName(hint.Path);
            if (name.Length == 0)
                return;

            var newList = new List<PathResolver.CacheHint>(2) { hint };
            var list = _cacheByName.GetOrAdd(name, newList);
            if (!ReferenceEquals(list, newList))
            {
                lock (list)
                {
                    list.Add(hint);
                }
            }
        }

        public IEnumerable<PathResolver.CacheHint> HitsForFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                yield break;
            if (_cacheByName.TryGetValue(fileName, out var list))
            {
                PathResolver.CacheHint[] snapshot;
                lock (list)
                {
                    snapshot = list.ToArray();
                }
                foreach (var hit in snapshot)
                    yield return hit;
            }
        }
    }

    public static PrefetchInfo? ParseFile(string pfPath, RiskContext? context = null, bool checkSignatures = true)
    {
        var attrs = CaptureFileAttributes(pfPath);

        byte[]? data = ForensicUtil.ReadAllBytesBounded(pfPath, MaxCompressedPfBytes);
        if (data is null || data.Length < MinPfSize)
            return null;

        
        
        
        
        int rawSize = data.Length;

        var info = ParseData(data, pfPath, attrs, context, checkSignatures);
        if (info is not null)
        {
            info.DedupFingerprint = $"{rawSize}|{info.PrefetchHash}|{info.HeaderExeName ?? ""}|{info.HashString ?? ""}";
        }
        return info;
    }

    internal static PrefetchInfo? ParseData(byte[] data, string pfPath, FileAttrs attrs,
        RiskContext? context = null, bool checkSignatures = true, bool wasDeleted = false)
    {
        
        
        
        
        PrefetchParseResult parsed = PrefetchCoreParser.Parse(data, pfPath);
        if (parsed.Artifact is null
            || parsed.State is PrefetchParseState.Invalid or PrefetchParseState.Corrupted
                or PrefetchParseState.UnknownVersion or PrefetchParseState.Unsupported)
        {
            return null;
        }

        PrefetchArtifact a = parsed.Artifact;
        int version = a.FormatVersion;
        int volumeCount = a.FileInfo.VolumesCount is > 0 and <= 256 ? a.FileInfo.VolumesCount : 0;

        var info = new PrefetchInfo
        {
            PfPath = pfPath,
            PfFileName = Path.GetFileName(pfPath),
            Version = version,
            SccaMagic = a.SccaMagic,
            FileSize = a.DeclaredFileSize,
            IsHidden = attrs.IsHidden,
            IsSystem = attrs.IsSystem,
            IsReadOnly = attrs.IsReadOnly,
            WasDeleted = wasDeleted,
            PrefetchHash = a.StoredPrefetchHash,
            HeaderExeName = a.HeaderExecutableName,
            VersionName = a.VersionName,
            RunCount = a.RunCount,
            VolumeCount = volumeCount,
            MultiVolume = volumeCount > 1,
            IsBootPrefetch = a.IsBootPrefetch,
            HashString = a.HashString,
            FileMetricsCount = a.Metrics.Count,
            TraceChainCount = a.TraceChains.Count,
            TotalBlockLoads = a.TraceChains.Sum(t => t.BlockLoads),
            MaxChainDepth = a.TraceChains.Count > 0 ? a.TraceChains.Max(t => t.Depth) : 0,
            DirectoryCount = a.Volumes.Sum(v => v.DirectoryStrings.Count),
            VolumeSerials = a.Volumes.Where(v => v.Serial != 0).Select(v => v.Serial).ToList(),
            VolumeDevicePaths = a.Volumes.Where(v => v.DevicePath.Length > 0).Select(v => v.DevicePath).ToList(),
            DirectoryNames = a.DirectoryStrings,
        };

        
        
        int metricsFieldCount = a.FileInfo.MetricsCount;
        info.IntegrityMismatch =
            (metricsFieldCount > 0 && a.FileStrings.Count > 0 && metricsFieldCount != a.FileStrings.Count)
            || (a.Metrics.Count > 0 && a.Metrics.Count != a.FileStrings.Count);

        
        
        
        
        int n = Math.Min(a.FileStrings.Count, MaxReferencedFiles);
        info.ReferencedFiles = new List<PrefetchFileRef>(n);
        for (int i = 0; i < n; i++)
        {
            PrefetchFileString s = a.FileStrings[i];
            PrefetchMetric? m = i < a.Metrics.Count ? a.Metrics[i] : null;
            info.ReferencedFiles.Add(new PrefetchFileRef
            {
                Path = VolumeMapper.ToDosPath(s.Value),
                MftReference = m is null ? 0 : (long)m.FileReference,
                FileMetricFlags = m?.Flags ?? 0,
                LoadAsExecutable = m?.LoadAsExecutable ?? false,
            });
        }

        info.ReferencedFileCount = info.ReferencedFiles.Count;
        info.ReferencedTotal = info.ReferencedFileCount;

        
        
        
        var validTimes = a.ExecutionTimes.Where(t => t.UnixSeconds != 0).ToList();
        info.LastExecutionTimes = new List<string>(validTimes.Count);
        foreach (PrefetchExecutionTime t in validTimes)
        {
            string formatted = ForensicUtil.UnixTimeToLocalString(t.UnixSeconds);
            if (formatted.Length > 0)
                info.LastExecutionTimes.Add(formatted);
        }
        info.FirstExecUnix = validTimes.Count > 0 ? validTimes[^1].UnixSeconds : 0;
        info.LastExecUnix = validTimes.Count > 0 ? validTimes[0].UnixSeconds : 0;

        if (info.RunCount <= 0 && validTimes.Count > 0)
            info.RunCount = validTimes.Count;

        
        
        
        string exeName = ExtractExeNameFromPfFilename(pfPath);
        string headerExe = info.HeaderExeName ?? "";
        string rawExe = FindExecutablePath(
            string.IsNullOrWhiteSpace(headerExe) ? exeName : headerExe,
            exeName,
            info.ReferencedFiles);
        var located = PathResolver.Resolve(exeName, rawExe, context);
        info.MainExecutablePath = located.Path;
        info.ResolvedFrom = located.ResolvedFrom;
        info.LastSeenPath = located.LastSeenPath;
        info.LastSeenSource = located.LastSeenSource;
        info.LastSeenTime = located.LastSeenTime;
        info.AmcachePublisher = located.Publisher;
        info.FileMissing = !located.Exists;
        info.RenamedFile = IsRenamedFile(exeName, info.HeaderExeName);
        info.PrefetchHashMismatch = DetectPrefetchHashMismatch(
            info.PrefetchHash, info.HashString, version);
        if (wasDeleted)
            info.PresenceKind = PresenceKind.DeletedPrefetch;
        else if (located.Exists)
            info.PresenceKind = PresenceKind.Present;
        else if (ForensicUtil.IsUnresolved(located.Path) || ForensicUtil.LooksDevicePath(located.Path))
            info.PresenceKind = PresenceKind.UnresolvedPath;
        else
            info.PresenceKind = PresenceKind.MissingFile;

        ApplyExecutableAttributes(info, located.Exists ? located.Path : "");

        var verdict = SignatureChecker.Evaluate(located.Exists ? located.Path : ForensicUtil.UnresolvedPath);
        info.MainSignatureStatus = located.Exists ? verdict.Status : SignatureStatus.NotFound;
        info.SignatureDetail = located.Exists ? verdict.Detail : PresenceDetail(info);

        
        
        if (!IsTrustedOsHost(info))
            PrefetchEnrichment.EnrichVersionInfo(info);

        if (checkSignatures)
            SignReferencedFiles(info, info.ReferencedFiles, context);

        var rules = new HashSet<string>(StringComparer.Ordinal);
        string mainExe = info.MainExecutablePath ?? "";
        if (located.Exists && !ShouldSkipDeepScan(mainExe, info.MainSignatureStatus))
        {
            
            
            if (context is null || context.TryReserveYara(mainExe))
            {
                foreach (string r in ScanYara(mainExe, deep: true))
                    rules.Add(r);
            }
        }

        if (checkSignatures)
        {
            
            
            const long refYaraSizeLimit = 8L * 1024 * 1024;

            var cheatSigned = info.ReferencedFiles
                .Where(r => r.SignatureStatus is SignatureStatus.Cheat or SignatureStatus.Fake)
                .ToList();
            info.CheatSignedReferenced = cheatSigned.Count;

            foreach (var r in info.ReferencedFiles)
            {
                if (!r.Suspicious)
                    continue;
                if (r.FileSize is > refYaraSizeLimit)
                    continue;
                if (ForensicUtil.IsOsBinaryPath(r.Path ?? ""))
                    continue;
                if (ShouldSkipDeepScan(r.Path ?? "", r.SignatureStatus))
                    continue;
                if (context is not null && !context.TryReserveYara(r.Path ?? ""))
                    continue; 
                foreach (string rule in ScanYara(r.Path ?? "", deep: true))
                    rules.Add(rule);
            }
            foreach (var r in cheatSigned)
            {
                if (r.FileSize is > refYaraSizeLimit)
                    continue;
                if (context is not null && !context.TryReserveYara(r.Path ?? ""))
                    continue;
                foreach (string rule in ScanYara(r.Path ?? "", deep: true))
                    rules.Add(rule);
            }
        }

        info.MatchedRules = rules.OrderBy(x => x, StringComparer.Ordinal).ToList();
        info.UnsignedReferenced = info.ReferencedFiles.Count(
            r => r.SignatureStatus is SignatureStatus.Unsigned or SignatureStatus.NotMZ);
        info.SuspiciousReferenced = info.ReferencedFiles.Count(r => r.Suspicious);

        ComputeRisk(info, context);
        TrimReferencedFilesForUi(info);
        return info;
    }

    public static void ScoreRecovered(PrefetchInfo info, RiskContext? context)
    {
        if (info is null)
            return;
        string mainExe = info.MainExecutablePath ?? "";
        var located = PathResolver.Resolve(
            ExtractExeNameFromPfFilename(info.PfFileName ?? ""),
            mainExe,
            context);
        info.MainExecutablePath = located.Path;
        info.ResolvedFrom = located.ResolvedFrom;
        info.LastSeenPath = located.LastSeenPath ?? info.LastSeenPath;
        info.LastSeenSource = located.LastSeenSource ?? info.LastSeenSource;
        info.LastSeenTime = located.LastSeenTime ?? info.LastSeenTime;
        info.AmcachePublisher = located.Publisher ?? info.AmcachePublisher;
        info.FileMissing = !located.Exists;
        info.PresenceKind = PresenceKind.DeletedPrefetch;
        info.WasDeleted = true;
        info.RenamedFile = IsRenamedFile(ExtractExeNameFromPfFilename(info.PfFileName ?? ""), info.HeaderExeName);

        var verdict = SignatureChecker.Evaluate(located.Exists ? located.Path : ForensicUtil.UnresolvedPath);
        info.MainSignatureStatus = located.Exists ? verdict.Status : SignatureStatus.NotFound;
        info.SignatureDetail = located.Exists ? verdict.Detail : PresenceDetail(info);
        if (located.Exists)
        {
            if (context is null || context.TryReserveYara(located.Path))
            {
                info.MatchedRules = ScanYara(located.Path, deep: true)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();
            }
        }
        ComputeRisk(info, context);
    }

    public static void RecomputeRisk(PrefetchInfo info, RiskContext? context)
    {
        if (info is null)
            return;
        ComputeRisk(info, context);
    }

    
    
    
    
    
    
    private static bool IsRenamedFile(string filenameExe, string? headerExe)
        => PrefetchFilenameParser.IsRenamed(filenameExe, headerExe);

    public static string ExtractExeNameFromPfFilename(string pfPath)
        => PrefetchFilenameParser.ExtractExecutable(Path.GetFileNameWithoutExtension(pfPath));

    
    
    
    
    
    
    
    
    internal static uint ComputePrefetchHash(string path)
        => PrefetchHash.Compute(path);

    internal static bool DetectPrefetchHashMismatch(uint stored, string? hashString, int version)
        => PrefetchHash.IsMismatched(stored, hashString, version);

    private static string FindExecutablePath(string exeName, string fallbackExeName, List<PrefetchFileRef> refs)
    {
        if (refs.Count == 0 || string.IsNullOrEmpty(exeName))
            return ForensicUtil.UnresolvedPath;

        
        
        foreach (string candidateName in new[] { exeName, fallbackExeName })
        {
            if (string.IsNullOrEmpty(candidateName))
                continue;
            string? hit = FindExactNameMatch(candidateName, refs);
            if (hit is not null)
                return hit;
        }

        string exeNorm = NormalizeName(exeName);
        string bestPath = ForensicUtil.UnresolvedPath;
        int bestScore = 0;

        foreach (var r in refs)
        {
            string fullPath = r.Path ?? "";
            if (fullPath.Length == 0)
                continue;
            string fileNorm = NormalizeName(Path.GetFileName(fullPath));
            int len = Math.Min(exeNorm.Length, fileNorm.Length);
            int score = 0;
            for (int i = 0; i < len; i++)
            {
                if (exeNorm[i] == fileNorm[i]) score++;
                else break;
            }
            if (fileNorm.Contains(exeNorm, StringComparison.Ordinal))
                score += 2;
            if (fileNorm.EndsWith("exe", StringComparison.Ordinal))
                score += 1;
            if (score > bestScore)
            {
                bestScore = score;
                bestPath = VolumeMapper.ToDosPath(fullPath);
            }
        }

        return bestScore == 0 ? ForensicUtil.UnresolvedPath : bestPath;
    }

    private static string? FindExactNameMatch(string name, List<PrefetchFileRef> refs)
    {
        var candidates = new List<string>(3) { name };
        if (!name.Contains('.', StringComparison.Ordinal))
        {
            candidates.Add(name + ".EXE");
            candidates.Add(name + ".exe");
        }

        foreach (var r in refs)
        {
            string fullPath = r.Path ?? "";
            if (fullPath.Length == 0)
                continue;
            string fileName = Path.GetFileName(fullPath);
            foreach (string expected in candidates)
            {
                if (fileName.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    return VolumeMapper.ToDosPath(fullPath);
            }
        }
        return null;
    }

    private static string NormalizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static bool IsMinecraftRelatedPath(string path) => PathClassifier.IsMinecraftRelatedPath(path);
    public static bool IsSuspiciousExecutablePath(string path) => PathClassifier.IsSuspiciousExecutablePath(path);
    public static bool IsSuspiciousReferenced(string path, SignatureStatus status) => PathClassifier.IsSuspiciousReferenced(path, status);

    internal static readonly HashSet<string> OsPrefetchHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSMAIN", "SUPERFETCH", "SVCHOST", "RUNTIMEBROKER", "SEARCHINDEXER",
        "MSMPENG", "NISSRV", "SECURITYHEALTHSERVICE", "SMARTSCREEN", "CONSENT",
        "TIWORKER", "TRUSTEDINSTALLER", "WUAUCLT", "DLLHOST", "TASKHOSTW",
        "SIHOST", "CTFMON", "FONDRVHOST", "SANDBOXBROKER", "BACKGROUNDTASKHOST",
        "APPLICATIONFRAMEHOST", "SHELLEXPERIENCEHOST", "STARTMENUEXPERIENCEHOST",
        "SEARCHAPP", "CONHOST", "CSRSS", "WININIT", "SERVICES", "LSASS", "SMSS"
    };

    internal static bool IsTrustedOsHost(PrefetchInfo info)
    {
        if (info.MainSignatureStatus != SignatureStatus.Signed)
            return false;
        string exe = Path.GetFileNameWithoutExtension(info.MainExecutablePath ?? "");
        if (exe.Length > 0 && OsPrefetchHosts.Contains(exe))
            return true;
        string pf = ExtractExeNameFromPfFilename(info.PfFileName ?? "");
        return pf.Length > 0 && OsPrefetchHosts.Contains(Path.GetFileNameWithoutExtension(pf));
    }

    private static void ComputeRisk(PrefetchInfo info, RiskContext? context) => RiskScorer.Score(info, context);

    internal static string PresenceDetail(PrefetchInfo info)
    {
        string kind = info.PresenceKind switch
        {
            PresenceKind.DeletedPrefetch => "Deleted .pf recovered from USN",
            PresenceKind.GhostLeftover => "No Prefetch — leftover cache evidence",
            PresenceKind.UnresolvedPath => "Unresolved device/volume path",
            _ => "Executable missing from disk"
        };
        var bits = new List<string>(4) { kind };
        if (!string.IsNullOrEmpty(info.LastSeenSource))
            bits.Add("last seen via " + info.LastSeenSource);
        if (!string.IsNullOrEmpty(info.LastSeenTime))
            bits.Add(info.LastSeenTime);
        if (!string.IsNullOrEmpty(info.AmcachePublisher))
            bits.Add("Amcache publisher: " + info.AmcachePublisher + " (not a signature)");
        bits.Add("signature not evaluated");
        return string.Join(" — ", bits);
    }

    public static void ClearCaches()
    {
        YaraCache.Clear();
        FileStatCache.Clear();
        SignatureChecker.ClearCache();
        ForensicUtil.ClearPathCaches();
    }

    
    
    

    
    
    
    
    
    
    
    
    private static void SignReferencedFiles(PrefetchInfo info, List<PrefetchFileRef> files, RiskContext? ctx)
    {
        int execLoadedCount = 0;
        foreach (var r in files)
        {
            string path = r.Path ?? "";
            r.SignatureStatus = SignatureChecker.GetSignatureStatus(path);
            bool isExecLoaded = r.LoadAsExecutable && !ForensicUtil.IsOsBinaryPath(path) &&
                r.SignatureStatus is SignatureStatus.Unsigned or SignatureStatus.NotMZ or
                    SignatureStatus.Cheat or SignatureStatus.Fake;
            if (isExecLoaded)
                execLoadedCount++;
            r.Suspicious = IsSuspiciousReferenced(path, r.SignatureStatus) || isExecLoaded;
        }
        info.ExecutableLoadedRefs = execLoadedCount;
    }

    internal struct FileStat
    {
        public bool Exists;
        public long Length;
        public string? Modified;
        public long WriteUtcTicks;
        public bool IsHidden, IsSystem, IsReadOnly;
    }
    private static readonly ConcurrentDictionary<string, FileStat> FileStatCache =
        new(StringComparer.OrdinalIgnoreCase);

    
    
    
    
    
    internal static FileStat GetCachedFileStat(string path)
    {
        if (FileStatCache.TryGetValue(path, out var stat))
            return stat;

        stat = default;
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists)
            {
                stat.Exists = true;
                stat.Length = fi.Length;
                stat.WriteUtcTicks = fi.LastWriteTimeUtc.Ticks;
                stat.Modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var attrs = fi.Attributes;
                stat.IsHidden = (attrs & FileAttributes.Hidden) != 0;
                stat.IsSystem = (attrs & FileAttributes.System) != 0;
                stat.IsReadOnly = (attrs & FileAttributes.ReadOnly) != 0;
            }
        }
        catch
        {
            
        }
        if (FileStatCache.Count >= FileStatCacheLimit)
            FileStatCache.Clear();
        FileStatCache[path] = stat;
        return stat;
    }

    private static List<string> ScanYara(string path, bool deep)
    {
        if (string.IsNullOrEmpty(path) || ForensicUtil.IsUnresolved(path))
            return new List<string>();
        if (YaraCache.TryGetValue(path, out var cached))
            return cached;

        var matches = YaraScanner.Scan(path, deep);
        if (YaraCache.Count >= YaraCacheLimit)
            YaraCache.Clear(); 
        YaraCache[path] = matches;
        return matches;
    }

    private static void ApplyExecutableAttributes(PrefetchInfo info, string mainExe)
    {
        
        
        info.IsHidden = false;
        info.IsSystem = false;
        info.IsReadOnly = false;
        if (string.IsNullOrEmpty(mainExe) || ForensicUtil.IsUnresolved(mainExe))
            return;

        string resolved = ForensicUtil.ResolveExistingPath(mainExe);
        var stat = GetCachedFileStat(resolved);
        if (!stat.Exists)
            return;
        info.IsHidden = stat.IsHidden;
        info.IsSystem = stat.IsSystem;
        info.IsReadOnly = stat.IsReadOnly;
    }

    private static bool ShouldSkipDeepScan(string path, SignatureStatus sig)
    {
        if (sig != SignatureStatus.Signed)
            return false;
        return ForensicUtil.IsOsBinaryPath(path);
    }

    internal struct FileAttrs { public bool IsHidden, IsSystem, IsReadOnly; }

    private static FileAttrs CaptureFileAttributes(string path)
    {
        var a = default(FileAttrs);
        try
        {
            var attrs = File.GetAttributes(path);
            a.IsHidden = (attrs & FileAttributes.Hidden) != 0;
            a.IsSystem = (attrs & FileAttributes.System) != 0;
            a.IsReadOnly = (attrs & FileAttributes.ReadOnly) != 0;
        }
        catch
        {
            
        }
        return a;
    }

    
    
    
    
    
    internal static void TrimReferencedFilesForUi(PrefetchInfo info, int keep = 48)
    {
        if (info.ReferencedFiles.Count <= keep)
        {
            foreach (var r in info.ReferencedFiles)
                EnrichReferencedFile(r);
            return;
        }

        info.ReferencedFiles = info.ReferencedFiles
            .OrderByDescending(r => r.Suspicious)
            .ThenByDescending(r => r.SignatureStatus is SignatureStatus.Cheat or SignatureStatus.Fake)
            .ThenByDescending(r => r.SignatureStatus is SignatureStatus.Unsigned or SignatureStatus.NotMZ)
            .Take(keep)
            .ToList();

        foreach (var r in info.ReferencedFiles)
            EnrichReferencedFile(r);
    }

    private static void EnrichReferencedFile(PrefetchFileRef r)
    {
        string? p = r.Path;
        if (string.IsNullOrEmpty(p))
            return;

        var stat = GetCachedFileStat(p);
        if (stat.Exists)
        {
            r.FileSize = stat.Length;
            r.Modified = stat.Modified;
        }

        
        
        
        bool execLoaded = r.LoadAsExecutable && !ForensicUtil.IsOsBinaryPath(r.Path ?? "");
        r.Suspicious = IsSuspiciousReferenced(r.Path ?? "", r.SignatureStatus) || execLoaded;
    }

}
