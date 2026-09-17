using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Serilog;



















internal static class PrefetchScanner
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(PrefetchScanner));
    private const int MaxFiles = 12000;

    public static async Task<PrefetchScanResult> ScanAsync(CancellationToken ct = default)
        => await Task.Run(() => Execute(ct), ct).ConfigureAwait(false);

    public static async IAsyncEnumerable<PrefetchInfo> ScanAsyncEnumerable(PrefetchParser.RiskContext ctx, IEnumerable<string> files, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var r = await Task.Run(() => PrefetchParser.ParseFile(f, ctx), ct);
            if (r is not null)
                yield return r;
        }
    }

    private static async Task<PrefetchScanResult> Execute(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new PrefetchScanResult();
        var dir = ForensicUtil.GetPrefetchDirectory();
        Log.Information("Prefetch scan starting {Dir}", dir);

        if (!TryValidateDirectory(dir, result))
        {
            result.ScanSeconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        var files = TryEnumerate(dir, result);
        if (files is null)
        {
            result.ScanSeconds = sw.Elapsed.TotalSeconds;
            return result;
        }
        if (files.Count > MaxFiles)
            files = files.OrderByDescending(f => { try { return new FileInfo(f).LastWriteTimeUtc; } catch { return DateTime.MinValue; } }).Take(MaxFiles).ToList();
        result.Total = files.Count;
        VolumeMapper.Refresh();
        if (ct.IsCancellationRequested)
            return Fail(result, "Scan cancelled before parse", sw);

        var ctx = BuildContext(ct, files.Count);
        if (ct.IsCancellationRequested)
            return Fail(result, "Cancelled while loading artifacts", sw);

        var (entries, failures) = ParseParallel(files, ctx, ct);
        result.Entries = entries;
        result.ParseFailures = failures;
        if (ct.IsCancellationRequested)
        {
            Aggregate(result, ctx);
            result.ScanSeconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        
        await JoinAmcache(ctx, ct).ConfigureAwait(false);
        if (ctx.AmcacheLoaded)
            RescoreForAmcache(result, ctx);

        RecoverDeleted(dir, result, ctx, ct);
        Deduplicate(result.Entries, ctx);
        CheckUsn(result);
        result.LogonTime = ctx.LogonTime;
        ArtifactCorrelator.Correlate(result, ctx, ctx.AmcacheEntries, ctx.ShimEntries, ctx.BamEntries);
        if (ctx.ShimLoaded && ctx.AmcacheLoaded && ctx.ShimCacheCount == 0 && ctx.AmcacheCount == 0 && (result.Total > 0 || result.RecoveredDeleted.Count > 0))
            result.EvidenceGap = true;
        Aggregate(result, ctx);
        ArtifactCorrelator.BuildSignals(result);
        result.ScanSeconds = sw.Elapsed.TotalSeconds;
        Log.Information("Prefetch scan done: {Total} entries {High} high-risk {Ghost} ghosts in {Seconds:0.0}s",
            result.Total, result.HighRisk, result.GhostCount, result.ScanSeconds);
        return result;
    }

    private static bool TryValidateDirectory(string dir, PrefetchScanResult r)
    {
        if (Directory.Exists(dir))
            return true;
        bool denied = false;
        try { File.GetAttributes(dir); }
        catch (UnauthorizedAccessException) { denied = true; }
        catch (Exception ex) { Log.Debug(ex, "Probe failed"); }
        if (denied || !ForensicUtil.IsAdministrator())
        {
            r.Error = "Administrator required to read Prefetch. Restart as Administrator.";
            r.RequiresAdmin = true;
        }
        else
        {
            r.Error = $"Prefetch folder not found: {dir}";
        }
        return false;
    }

    private static List<string>? TryEnumerate(string dir, PrefetchScanResult r)
    {
        try
        {
            try
            {
                using var e = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
                e.MoveNext();
            }
            catch (UnauthorizedAccessException)
            {
                r.Error = "Administrator required.";
                r.RequiresAdmin = true;
                return null;
            }
            return Directory.EnumerateFiles(dir, "*.pf", new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.None,
                IgnoreInaccessible = true,
                RecurseSubdirectories = false
            }).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            r.Error = "Administrator required.";
            r.RequiresAdmin = true;
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Enumerate failed");
            r.Error = ex.Message;
            return null;
        }
    }

    private static PrefetchScanResult Fail(PrefetchScanResult r, string msg, Stopwatch sw)
    {
        r.Error = msg;
        r.ScanSeconds = sw.Elapsed.TotalSeconds;
        return r;
    }

    private static (List<PrefetchInfo> entries, int failures) ParseParallel(List<string> files, PrefetchParser.RiskContext ctx, CancellationToken ct)
    {
        var parsed = new PrefetchInfo?[files.Count];
        int fails = 0;
        
        
        
        
        int dop = Math.Clamp(Environment.ProcessorCount, 2, 4);
        try
        {
            Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct }, i =>
            {
                try
                {
                    parsed[i] = PrefetchParser.ParseFile(files[i], ctx);
                    if (parsed[i] is null)
                        Interlocked.Increment(ref fails);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref fails);
                    Log.Debug(ex, "Parse failed {F}", files[i]);
                }
            });
        }
        catch (OperationCanceledException)
        {
            Log.Information("Parse cancelled");
        }
        var list = parsed.Where(p => p is not null).Cast<PrefetchInfo>()
            .OrderByDescending(p => p.RiskScore)
            .ThenBy(p => p.PfFileName)
            .ToList();
        return (list, fails);
    }

    private static void RecoverDeleted(string dir, PrefetchScanResult r, PrefetchParser.RiskContext ctx, CancellationToken ct)
    {
        try
        {
            r.RecoveredDeleted = PrefetchRecovery.RecoverDeletedPf(Path.GetPathRoot(dir), r.Entries, ctx, ct);
            foreach (var x in r.RecoveredDeleted)
                x.WasDeleted = true;
        }
        catch (OperationCanceledException)
        {
            Log.Information("Recovery cancelled");
            r.RecoveredDeleted = new();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Recovery failed");
            r.RecoveredDeleted = new();
        }
    }

    private static void Deduplicate(List<PrefetchInfo> entries, PrefetchParser.RiskContext ctx)
    {
        if (entries.Count == 0)
            return;

        
        
        
        
        var byFp = new Dictionary<string, List<PrefetchInfo>>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.DedupFingerprint))
                continue;
            if (!byFp.TryGetValue(e.DedupFingerprint, out var l))
                byFp[e.DedupFingerprint] = l = new(2);
            l.Add(e);
        }

        foreach (var group in byFp.Values)
        {
            if (group.Count < 2)
                continue;

            var byHash = new Dictionary<string, List<PrefetchInfo>>(StringComparer.Ordinal);
            foreach (var e in group)
            {
                string? hash = HashPfFile(e.PfPath);
                if (hash is null)
                    continue;
                e.Sha256 = hash;
                if (!byHash.TryGetValue(hash, out var l))
                    byHash[hash] = l = new(2);
                l.Add(e);
            }

            foreach (var g in byHash.Values)
            {
                if (g.Count < 2)
                    continue;
                foreach (var e in g)
                {
                    e.DuplicateOf = string.Join(", ", g.Where(x => !ReferenceEquals(x, e)).Select(x => x.PfFileName ?? "").Where(s => s.Length > 0));
                    PrefetchParser.RecomputeRisk(e, ctx);
                }
            }
        }
    }

    private static string? HashPfFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        byte[]? data = ForensicUtil.ReadAllBytesBounded(path, PrefetchParser.MaxCompressedPfBytes);
        if (data is null)
            return null;
        return Convert.ToHexString(SHA256.HashData(data));
    }

    private static void CheckUsn(PrefetchScanResult r)
    {
        try
        {
            var s = new UsnJournalReader().CheckIntegrity(ForensicUtil.GetWindowsDriveLetter());
            r.UsnJournalEnabled = s.JournalEnabled;
            r.UsnJournalRecreated = s.JournalRecreated;
            r.UsnJournalDisabled = s.JournalDisabled;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "USN check failed");
        }
    }

    private static PrefetchParser.RiskContext BuildContext(CancellationToken ct, int fileCount)
    {
        
        
        
        var c = new PrefetchParser.RiskContext
        {
            LogonTime = ForensicUtil.GetLogonUnixTime()
        };
        LoadShim(c);
        if (ct.IsCancellationRequested)
            return c;

        
        
        
        try
        {
            _ = Task.Run(() =>
            {
                LoadAmcache(c);
                LoadBam(c);
            }, ct);
        }
        catch
        {
            
        }
        return c;
    }

    private static async Task JoinAmcache(PrefetchParser.RiskContext c, CancellationToken ct)
    {
        
        
        try
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(45), ct);
            var done = c.AmcacheLoadDone;
            var completed = await Task.WhenAny(done, timeout).ConfigureAwait(false);
            if (completed != done)
                Log.Information("Amcache load did not complete within timeout");
        }
        catch (OperationCanceledException)
        {
            
        }
    }

    private static void RescoreForAmcache(PrefetchScanResult r, PrefetchParser.RiskContext ctx)
    {
        foreach (var e in r.Entries)
            PrefetchParser.RecomputeRisk(e, ctx);
    }

    private static void LoadShim(PrefetchParser.RiskContext c)
    {
        try
        {
            var e = new ShimCacheReader().LoadLive();
            c.ShimLoaded = true;
            c.ShimCacheCount = e.Count;
            c.ShimEntries = e;
            foreach (var x in e)
            {
                c.ShimIndex.Add(x.Path);
                c.IndexCacheHint(new PathResolver.CacheHint { Path = VolumeMapper.ToDosPath(x.Path), Source = "ShimCache", Timestamp = x.Modified });
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Shim failed");
        }
    }

    private static void LoadBam(PrefetchParser.RiskContext c)
    {
        try
        {
            var r = new BamReader();
            var e = r.LoadLive();
            c.BamLoaded = r.LastLoadSucceeded;
            c.BamCount = e.Count;
            c.BamEntries = e;
            foreach (var x in e)
            {
                c.BamIndex.Add(x.Path);
                c.IndexCacheHint(new PathResolver.CacheHint
                {
                    Path = VolumeMapper.ToDosPath(x.Path),
                    Source = "BAM",
                    Timestamp = x.LastExecution
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "BAM failed");
        }
    }

    private static void LoadAmcache(PrefetchParser.RiskContext c)
    {
        try
        {
            var r = new AmcacheReader();
            var e = r.LoadLive();
            c.AmcacheLoaded = r.LastLoadSucceeded;
            c.AmcacheCount = e.Count;
            c.AmcacheEntries = e;
            foreach (var x in e)
            {
                c.AmcacheIndex.Add(x.Path);
                c.IndexCacheHint(new PathResolver.CacheHint
                {
                    Path = VolumeMapper.ToDosPath(x.Path),
                    Source = "Amcache",
                    Timestamp = x.LinkDate,
                    Publisher = x.Publisher
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Amcache failed");
        }
        finally
        {
            c.SignalAmcacheLoadDone();
        }
    }

    private static IEnumerable<PrefetchInfo> All(PrefetchScanResult r)
    {
        foreach (var e in r.Entries)
            yield return e;
        foreach (var e in r.RecoveredDeleted)
            yield return e;
        foreach (var e in r.GhostEntries)
            yield return e;
    }

    private static void Aggregate(PrefetchScanResult r, PrefetchParser.RiskContext c)
    {
        var cards = All(r).ToList();
        r.HighRisk = cards.Count(e => e.RiskScore >= 50);
        r.Unsigned = cards.Count(e => e.MainSignatureStatus == SignatureStatus.Unsigned);
        r.CheatSig = cards.Count(e => e.MainSignatureStatus == SignatureStatus.Cheat);
        r.FakeSig = cards.Count(e => e.MainSignatureStatus == SignatureStatus.Fake);
        r.NotFound = cards.Count(e => e.FileMissing || e.PresenceKind is not PresenceKind.Present);
        r.YaraMatch = cards.Count(e => e.MatchedRules.Count > 0);
        r.ShimCacheCount = c.ShimCacheCount;
        r.AmcacheCount = c.AmcacheCount;
        r.BamCount = c.BamCount;
        r.DeletedCount = r.RecoveredDeleted.Count;
        r.DuplicateCount = r.Entries.Count(e => !string.IsNullOrEmpty(e.DuplicateOf));
        r.LeftoverCount = cards.Count(e => e.ArtifactLeftover || e.PresenceKind == PresenceKind.GhostLeftover);
        r.TimestompCount = cards.Count(e => e.Timestomped);
        r.GhostCount = r.GhostEntries.Count > 0 ? r.GhostEntries.Count : r.GhostArtifacts.Count;
        r.AnalysisTruncated = cards.Any(e => e.AnalysisTruncated);
    }
}
