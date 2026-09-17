using System.Reflection;
using System.Security.Cryptography;
using System.Text;



public sealed class BamParseOptions
{
    
    public bool IncludeLegacyLayout { get; init; } = true;
    
    public bool IncludeStateLayout { get; init; } = true;
    
    public bool IncludeDam { get; init; } = true;
    
    public int MaxSidsPerLayout { get; init; } = 4096;
    
    public int MaxValuesPerSid { get; init; } = 65536;
    
    public Func<string, string?>? SidResolver { get; init; }
}








internal static class BamCoreParser
{
    public const string Version = "1.0";

    private static readonly string[] LayoutRoots =
    {
        "Services\\bam\\State\\UserSettings",   
        "Services\\bam\\UserSettings",           
        "Services\\dam\\State\\UserSettings",    
        "Services\\dam\\UserSettings",           
    };

    private const string ServicesRoot = "Services";
    private const uint RegBinary = 3;

    public static BamParseResult ParseLive(BamParseOptions? options = null)
    {
        var live = new LiveBamHiveSource();
        var opts = options ?? new BamParseOptions();
        var result = Parse(live, opts);
        result.SourceHivePath = null;
        return result;
    }

    
    public static BamParseResult ParseOffline(byte[] hiveBytes, string? sourcePath = null, BamParseOptions? options = null)
    {
        var result = ParseOfflineInternal(hiveBytes, sourcePath, options);
        if (hiveBytes is { Length: > 0 })
            result.SourceHiveSha256 = Convert.ToHexString(SHA256.HashData(hiveBytes));
        return result;
    }

    private static BamParseResult ParseOfflineInternal(byte[] hiveBytes, string? sourcePath, BamParseOptions? options)
    {
        var reader = BamRegfReader.TryOpen(hiveBytes);
        if (reader is null)
        {
            var invalid = new BamParseResult
            {
                State = BamParseState.Invalid,
                Confidence = 0,
                SourceName = sourcePath is null ? "OfflineHive" : $"OfflineHive:{sourcePath}",
                SourceHivePath = sourcePath,
            };
            invalid.Errors.Add(new BamDiagnostic(BamErrorCode.Hive, "Not a readable registry hive (no regf base block)"));
            return invalid;
        }
        return Parse(new OfflineBamHiveSource(reader, sourcePath), options ?? new BamParseOptions());
    }

    
    public static BamParseResult Parse(IBamHiveSource source, BamParseOptions options)
    {
        var result = new BamParseResult
        {
            State = BamParseState.Success,
            Confidence = 0,
            SourceName = source.SourceName,
        };

        var controlSets = DiscoverControlSets(source, result);
        if (controlSets.Count == 0)
        {
            
            
            controlSets.Add(new BamControlSetInfo { Name = "", Role = ControlSetRole.Other });
            result.Warnings.Add(new BamDiagnostic(BamErrorCode.ControlSet,
                "No ControlSet keys under the hive root; scanning at the root level"));
        }
        result.ControlSets.AddRange(controlSets);

        var rawRecords = new List<BamSourceRecord>();
        int totalSids = 0;
        foreach (var cs in controlSets)
        {
            int csRecords = 0;
            foreach ((BamLayout layout, string root) in EnabledLayoutRoots(options))
            {
                string keyPath = cs.Name.Length == 0 ? root : cs.Name + "\\" + root;
                if (!source.KeyExists(keyPath))
                    continue;
                foreach (string sidName in source.EnumerateSubKeys(keyPath))
                {
                    if (sidName.Length == 0 || totalSids++ >= options.MaxSidsPerLayout * 4)
                        break;
                    string sidPath = keyPath + "\\" + sidName;
                    var sid = BamSidParser.Parse(sidName);
                    int parsed = ReadSidValues(source, sidPath, cs, layout, sid, options, rawRecords, result);
                    csRecords += parsed;
                    if (!sid.Valid)
                        result.Warnings.Add(new BamDiagnostic(BamErrorCode.Sid,
                            $"SID '{sidName}' under {keyPath} is malformed: {sid.InvalidReason}"));
                }
            }
            cs.BamEntryCount = csRecords;
        }

        if (rawRecords.Count == 0 && result.Errors.Count == 0)
        {
            result.State = BamParseState.Success;
            result.Warnings.Add(new BamDiagnostic(BamErrorCode.Layout, "No BAM entries found in any known layout"));
        }

        
        var grouped = rawRecords.GroupBy(r => (Sid: r.UserSidRaw, Path: r.RawValueName.ToLowerInvariant()));
        foreach (var group in grouped)
        {
            var artifact = MergeRecords(group.Key.Sid, group.ToList(), options.SidResolver);
            result.Artifacts.Add(artifact);
            CountArtifact(result.Statistics, artifact);
        }
        result.Statistics.TotalSidKeys = totalSids;
        result.Statistics.TotalSourceRecords = rawRecords.Count;
        result.Statistics.DuplicateEntries = result.Artifacts.Count(a => a.Sources.Count > 1
            && a.Sources.Select(s => (s.RawValueName, s.Binary.UnixSeconds)).Distinct().Count() == 1);
        result.Statistics.ConflictingEntries = result.Artifacts.Count(a => a.Conflicts.Count > 0);
        result.Statistics.InvalidEntries = result.Artifacts.Count(a =>
            a.Sources.All(s => s.Binary.State == BamValueState.UnexpectedLength));
        result.Statistics.UnresolvedPaths = result.Artifacts.Count(a => a.Path.Resolution == PathResolutionState.Unresolved);

        
        
        
        var perControlSet = result.Artifacts
            .SelectMany(a => a.Sources)
            .GroupBy(s => s.ControlSet)
            .ToDictionary(g => g.Key, g => g
                .GroupBy(s => s.RawValueName.ToLowerInvariant())
                .ToDictionary(pg => pg.Key,
                    pg => pg.FirstOrDefault(x => x.Binary.UnixSeconds != 0)?.Binary.UnixSeconds,
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var csNames = perControlSet.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < csNames.Count; i++)
        {
            for (int j = i + 1; j < csNames.Count; j++)
            {
                var a = perControlSet[csNames[i]];
                var b = perControlSet[csNames[j]];
                var allPaths = a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (string path in allPaths)
                {
                    bool inA = a.TryGetValue(path, out long? ta);
                    bool inB = b.TryGetValue(path, out long? tb);
                    BamDifferenceType type;
                    if (inA && inB)
                    {
                        if (ta is null && tb is null)
                            continue;
                        if (ta is null)
                            type = BamDifferenceType.PresentOnlyInB;
                        else if (tb is null)
                            type = BamDifferenceType.PresentOnlyInA;
                        else if (ta != tb)
                            type = BamDifferenceType.TimestampChanged;
                        else
                            continue;
                    }
                    else if (inA)
                    {
                        type = BamDifferenceType.PresentOnlyInA;
                    }
                    else
                    {
                        type = BamDifferenceType.PresentOnlyInB;
                    }
                    result.ControlSetDifferences.Add(new BamControlSetDifference
                    {
                        Path = path,
                        ControlSetA = csNames[i],
                        TimestampA = ta,
                        ControlSetB = csNames[j],
                        TimestampB = tb,
                        Type = type,
                    });
                }
            }
        }

        result.State = result.Errors.Count > 0 ? BamParseState.Partial : result.State;
        result.Confidence = BamConfidence.Adjust(
            BamConfidence.FromState(result.State),
            result.Errors.Count,
            result.Statistics.ConflictingEntries,
            result.Statistics.UnresolvedPaths > 0);
        return result;
    }

    
    
    
    
    
    public static BamParseResult RecoverDeleted(byte[] hiveBytes, string? sourcePath = null)
    {
        var result = new BamParseResult
        {
            State = BamParseState.Recovered,
            Confidence = BamConfidence.FromState(BamParseState.Recovered),
            SourceName = sourcePath is null ? "RecoveredHive" : $"RecoveredHive:{sourcePath}",
            SourceHivePath = sourcePath,
        };
        if (hiveBytes is { Length: > 0 })
            result.SourceHiveSha256 = Convert.ToHexString(SHA256.HashData(hiveBytes));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int candidates = 0;

        
        
        var reader = BamRegfReader.TryOpen(hiveBytes);
        if (reader is not null)
        {
            foreach ((int offset, byte[] payload) in reader.EnumerateFreeCells())
            {
                foreach (string path in ExtractPathsFromBytes(payload))
                {
                    if (seen.Add(path))
                    {
                        result.RecoveredPaths.Add(new RecoveredBamPath { RawPath = path, Offset = offset, Source = "FreeCell" });
                        result.Warnings.Add(new BamDiagnostic(BamErrorCode.Recovery,
                            $"Recovered BAM path '{path}' from free cell at 0x{offset:X}"));
                        candidates++;
                    }
                }
            }
        }

        
        
        foreach (string path in ExtractPathsFromBytes(hiveBytes))
        {
            if (seen.Add(path))
            {
                result.RecoveredPaths.Add(new RecoveredBamPath { RawPath = path, Offset = 0, Source = "RawScan" });
                result.Warnings.Add(new BamDiagnostic(BamErrorCode.Recovery,
                    $"Recovered BAM path '{path}' from raw hive scan"));
                candidates++;
            }
        }

        result.Statistics.RecoveredEntries = seen.Count;
        result.Statistics.TotalSourceRecords = seen.Count;
        result.Statistics.ValidEntries = seen.Count;
        result.Confidence = BamConfidence.Adjust(result.Confidence, 0, 0, false);
        if (candidates == 0)
            result.State = BamParseState.Invalid;
        return result;
    }

    
    
    

    private static List<BamControlSetInfo> DiscoverControlSets(IBamHiveSource source, BamParseResult result)
    {
        var sets = new List<BamControlSetInfo>();
        var roles = new Dictionary<string, ControlSetRole>(StringComparer.OrdinalIgnoreCase);

        
        
        
        if (source.KeyExists("Select"))
        {
            foreach (var v in source.EnumerateValues("Select"))
            {
                ControlSetRole role = v.Name.ToLowerInvariant() switch
                {
                    "current" => ControlSetRole.Current,
                    "default" => ControlSetRole.Default,
                    "lastknowngood" => ControlSetRole.LastKnownGood,
                    _ => ControlSetRole.Other,
                };
                if (v.Type == RegBinary && v.Data.Length >= 4)
                {
                    uint index = BitConverter.ToUInt32(v.Data, 0);
                    if (index > 0 && index <= 999)
                    {
                        string key = $"ControlSet{index:D3}";
                        if (!roles.TryGetValue(key, out ControlSetRole existing)
                            || RolePriority(role) > RolePriority(existing))
                            roles[key] = role;
                    }
                }
            }
        }

        foreach (string name in source.EnumerateSubKeys(""))
        {
            if (name.StartsWith("ControlSet", StringComparison.OrdinalIgnoreCase) && name.Length > 10)
            {
                var role = roles.TryGetValue(name, out var r) ? r : ControlSetRole.Other;
                sets.Add(new BamControlSetInfo { Name = name, Role = role });
            }
        }

        
        if (sets.Count == 0 && source.KeyExists(ServicesRoot))
            sets.Add(new BamControlSetInfo { Name = "", Role = ControlSetRole.Other });

        return sets.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int RolePriority(ControlSetRole role) => role switch
    {
        ControlSetRole.Current => 3,
        ControlSetRole.Default => 2,
        ControlSetRole.LastKnownGood => 1,
        _ => 0,
    };

    private static IEnumerable<(BamLayout Layout, string Root)> EnabledLayoutRoots(BamParseOptions options)
    {
        if (options.IncludeStateLayout)
            yield return (BamLayout.StateBamUserSettings, LayoutRoots[0]);
        if (options.IncludeLegacyLayout)
            yield return (BamLayout.LegacyBamUserSettings, LayoutRoots[1]);
        if (options.IncludeDam)
        {
            if (options.IncludeStateLayout)
                yield return (BamLayout.StateDamUserSettings, LayoutRoots[2]);
            if (options.IncludeLegacyLayout)
                yield return (BamLayout.LegacyDamUserSettings, LayoutRoots[3]);
        }
    }

    
    
    

    private static int ReadSidValues(IBamHiveSource source, string sidPath,
        BamControlSetInfo cs, BamLayout layout, BamSid sid, BamParseOptions options,
        List<BamSourceRecord> records, BamParseResult result)
    {
        int parsed = 0;
        var values = source.EnumerateValues(sidPath);
        foreach (var value in values)
        {
            if (parsed >= options.MaxValuesPerSid)
            {
                result.Warnings.Add(new BamDiagnostic(BamErrorCode.Bounds,
                    $"SID '{sidPath}' exceeds the {options.MaxValuesPerSid}-value cap; further values skipped"));
                break;
            }
            if (value.Name.Equals("SequenceNumber", StringComparison.OrdinalIgnoreCase) ||
                value.Name.Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                continue; 
            }

            if (value.Type != RegBinary)
            {
                result.Warnings.Add(new BamDiagnostic(BamErrorCode.ValueType,
                    $"'{sidPath}\\{value.Name}' is type {value.Type}, not REG_BINARY — not parsed as a BAM entry"));
                continue;
            }

            var binary = BamBinaryValueParser.Parse(value.Data);
            var path = BamPathParser.Parse(value.Name);

            if (path.Type == BamPathType.Malformed)
                result.Warnings.Add(new BamDiagnostic(BamErrorCode.Path,
                    $"'{sidPath}' value name is not a path: '{value.Name}'"));
            if (binary.TimestampValidity is TimestampValidity.Invalid or TimestampValidity.Unknown)
                result.Statistics.InvalidTimestamps++;
            if (path.Resolution == PathResolutionState.Unresolved)
                result.Statistics.UnresolvedPaths++;

            var record = new BamSourceRecord
            {
                ControlSet = cs.Name,
                ControlSetRole = cs.Role,
                Layout = layout,
                RegistryPath = sidPath,
                UserSidRaw = sid.Raw,
                RawValueName = value.Name,
                ValueType = value.Type,
                ValueDataLength = value.Data.Length,
                ValueDataHex = Convert.ToHexString(value.Data),
                Binary = binary,
                Evidence = BamEvidenceLevel.Exact,
            };
            if (binary.State != BamValueState.ValidKnownLength)
                record.Diagnostics.Add(new BamDiagnostic(BamErrorCode.ValueLength,
                    $"Value length {value.Data.Length} is {binary.State}"));
            if (binary.ReservedRegionNonZero)
                record.Diagnostics.Add(new BamDiagnostic(BamErrorCode.Integrity,
                    $"Reserved region 0x{binary.ReservedRegion:X16} is non-zero (tamper indicator, not proof)"));
            if (!sid.Valid)
                record.Diagnostics.Add(new BamDiagnostic(BamErrorCode.Sid, $"Malformed SID: {sid.InvalidReason}"));

            records.Add(record);
            parsed++;
        }
        return parsed;
    }

    
    
    

    private static BamArtifact MergeRecords(string sidRaw, List<BamSourceRecord> records,
        Func<string, string?>? sidResolver)
    {
        var path = BamPathParser.Parse(records[0].RawValueName);
        var sid = BamSidParser.Parse(sidRaw);

        var validTimes = records
            .Select(r => r.Binary)
            .Where(b => b.UnixSeconds != 0)
            .OrderByDescending(b => b.UnixSeconds)
            .ToList();

        var conflicts = new List<BamConflict>();
        var byTime = records.Where(r => r.Binary.UnixSeconds != 0)
            .GroupBy(r => r.Binary.UnixSeconds)
            .ToList();
        if (byTime.Count > 1)
        {
            var first = byTime[0].First();
            foreach (var other in byTime.Skip(1))
            {
                conflicts.Add(new BamConflict
                {
                    TimestampA = first.Binary.UnixSeconds,
                    SourceA = $"{first.ControlSet}/{first.Layout}",
                    TimestampB = other.First().Binary.UnixSeconds,
                    SourceB = $"{other.First().ControlSet}/{other.First().Layout}",
                });
            }
        }

        var best = records.OrderByDescending(r => r.Binary.UnixSeconds).FirstOrDefault()
                   ?? records[0];
        bool anyWindowsApp = records.Any(r => r.Binary.IsWindowsApp);
        bool anyExact = records.Any(r => r.Evidence == BamEvidenceLevel.Exact);

        var userName = sid.WellKnownName ?? sidResolver?.Invoke(sidRaw);
        AttributionConfidence attribution;
        if (sid.Valid && !string.IsNullOrEmpty(userName))
            attribution = AttributionConfidence.High;
        else if (sid.Valid)
            attribution = AttributionConfidence.Medium;
        else
            attribution = AttributionConfidence.Low;

        EvidenceQuality evidence = records.Max(r => r.Evidence) switch
        {
            BamEvidenceLevel.Exact => EvidenceQuality.Direct,
            BamEvidenceLevel.Validated => EvidenceQuality.Strong,
            BamEvidenceLevel.Recovered => EvidenceQuality.Supporting,
            BamEvidenceLevel.Inferred => EvidenceQuality.Contextual,
            _ => EvidenceQuality.Unknown,
        };
        _ = anyExact;

        double confidence = 99;
        if (records.Any(r => r.Binary.State != BamValueState.ValidKnownLength))
            confidence -= 10;
        if (conflicts.Count > 0)
            confidence -= 10;
        if (path.Resolution == PathResolutionState.Unresolved)
            confidence -= 5;
        if (!sid.Valid)
            confidence -= 10;
        if (records.All(r => r.Evidence == BamEvidenceLevel.Recovered))
            confidence = 55;

        var artifact = new BamArtifact
        {
            NormalizedPath = path.Normalized,
            Path = path,
            UserSidRaw = sid.Raw,
            UserSid = sid,
            UserName = userName,
            Attribution = attribution,
            LastExecutionUnix = validTimes.Count > 0 ? validTimes[0].UnixSeconds : null,
            LastExecutionUtc = validTimes.Count > 0 ? validTimes[0].UtcString : null,
            TimestampValidity = best.Binary.TimestampValidity,
            ValueState = records.Max(r => r.Binary.State),
            IsWindowsApp = anyWindowsApp,
            Evidence = evidence,
            Confidence = Math.Clamp(confidence, 0, 100),
        };
        artifact.Sources.AddRange(records);
        artifact.Conflicts.AddRange(conflicts);
        return artifact;
    }

    private static void CountArtifact(BamStatistics stats, BamArtifact artifact)
    {
        stats.ValidEntries++;
        foreach (var s in artifact.Sources)
        {
            if (s.Layout is BamLayout.LegacyBamUserSettings or BamLayout.LegacyDamUserSettings)
                stats.LegacyLayoutEntries++;
            else
                stats.StateLayoutEntries++;
        }
    }

    
    
    

    private static readonly byte[] AsciiMarker = Encoding.ASCII.GetBytes(@"\Device\HarddiskVolume");

    private static IEnumerable<string> ExtractPathsFromBytes(byte[] data)
    {
        if (data is null || data.Length < 8)
            yield break;

        
        foreach (int marker in FindAll(data, AsciiMarker))
        {
            string? path = ReadAsciiPath(data, marker);
            if (path is not null && LooksExecutablePath(path))
                yield return path;
        }

        
        byte[] wideMarker = Encoding.Unicode.GetBytes(@"\Device\HarddiskVolume");
        foreach (int marker in FindAll(data, wideMarker))
        {
            string? path = ReadWidePath(data, marker);
            if (path is not null && LooksExecutablePath(path))
                yield return path;
        }

        
        foreach (int marker in FindAll(data, Encoding.ASCII.GetBytes(@"C:\")))
        {
            if (marker + 3 < data.Length && IsAsciiChar(data[marker + 3]))
                continue; 
            string? path = ReadAsciiPath(data, marker);
            if (path is not null && LooksExecutablePath(path))
                yield return path;
        }
    }

    private static bool LooksExecutablePath(string path)
        => path.Length is > 8 and <= 512 && path.Contains('\\');

    private static string? ReadAsciiPath(byte[] data, int start)
    {
        int end = start;
        while (end < data.Length && end - start <= 512 && data[end] != 0 && !IsControl(data[end]))
            end++;
        if (end - start < 8)
            return null;
        try
        {
            return Encoding.ASCII.GetString(data, start, end - start);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadWidePath(byte[] data, int start)
    {
        if (start + 2 > data.Length)
            return null;
        int end = start;
        while (end + 1 < data.Length && end - start <= 1024 && !(data[end] == 0 && data[end + 1] == 0))
            end += 2;
        if (end - start < 16)
            return null;
        try
        {
            return Encoding.Unicode.GetString(data, start, end - start);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<int> FindAll(byte[] data, byte[] needle)
    {
        for (int i = 0; i <= data.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (data[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                yield return i;
        }
    }

    private static bool IsControl(byte b) => b < 0x20;
    private static bool IsAsciiChar(byte b) => b is >= 0x20 and < 0x7F;
}
