using System.Text.Json;
using System.Text.Json.Serialization;








public sealed class PrefetchArtifact
{
    public PrefetchKind Kind { get; init; }
    public PrefetchParseState State { get; init; }
    public double Confidence { get; init; }

    
    public int FormatVersion { get; init; }
    public string VersionName { get; init; } = "";
    public PrefetchVariant Variant { get; init; }
    public SupportLevel Support { get; init; }
    public int SccaMagic { get; init; }
    public int DeclaredFileSize { get; init; }
    public SizeValidation SizeState { get; init; }
    public string HeaderExecutableName { get; init; } = "";
    public uint StoredPrefetchHash { get; init; }
    public string? HashString { get; init; }
    public bool IsBootPrefetch { get; init; }

    
    public PrefetchFileInfo FileInfo { get; init; } = new();
    public int RunCount { get; init; }
    public bool RunCountPlausible { get; init; }
    public List<PrefetchExecutionTime> ExecutionTimes { get; init; } = new();
    public List<PrefetchMetric> Metrics { get; init; } = new();
    public List<PrefetchTraceChain> TraceChains { get; init; } = new();
    public List<PrefetchFileString> FileStrings { get; init; } = new();
    public List<PrefetchVolume> Volumes { get; init; } = new();
    public List<string> DirectoryStrings { get; init; } = new();

    
    public PrefetchIdentity Identity { get; init; } = new();

    
    public List<PrefetchRegion> Regions { get; init; } = new();
    public List<PrefetchRegionOverlap> Overlaps { get; init; } = new();
    public List<PrefetchUnknownRegion> UnknownRegions { get; init; } = new();
    public string? Compression { get; init; }
    public int CompressedPayloadSize { get; init; }

    
    public PrefetchValidation Validation { get; init; } = new();

    public string ToDiagnosticJson()
        => JsonSerializer.Serialize(this, SnapshotJson.Options);
}


public enum SizeValidation
{
    Unverifiable,
    SizeMatches,
    SizeMismatch,
    Truncated,
    TrailingData,
}


public sealed class PrefetchFileInfo
{
    public int MetricsOffset { get; init; }
    public int MetricsCount { get; init; }
    public int TraceChainsOffset { get; init; }
    public int TraceChainsCount { get; init; }
    public int StringsOffset { get; init; }
    public int StringsSize { get; init; }
    public int VolumesOffset { get; init; }
    public int VolumesCount { get; init; }
    public bool MetricsCountMatchesStrings { get; init; }
}


public sealed class PrefetchExecutionTime
{
    public int Index { get; init; }
    public ulong RawFileTime { get; init; }
    
    public long UnixSeconds { get; init; }
    
    public string? LocalTime { get; init; }
    public EvidenceLevel Evidence { get; init; }
}


public sealed class PrefetchMetric
{
    public int Index { get; init; }
    public int Offset { get; init; }
    public int Size { get; init; }
    
    public ulong FileReference { get; init; }
    
    public long MftEntry { get; init; }
    
    public int MftSequence { get; init; }
    public uint Flags { get; init; }
    public bool LoadAsExecutable { get; init; }
    
    public int ChainIndex { get; init; }
}


public sealed class PrefetchTraceChain
{
    public int Index { get; init; }
    public int Offset { get; init; }
    public int EntrySize { get; init; }
    public int BlockLoads { get; init; }
    
    public int Depth { get; init; }
    
    public int MetricsReference { get; init; }
}


public sealed class PrefetchFileString
{
    public int Index { get; init; }
    public int Offset { get; init; }
    public int Length { get; init; }
    public string Value { get; init; } = "";
    public StringValidation Validation { get; init; }
}

public enum StringValidation
{
    Valid,
    ContainsControlChars,
    NotNullTerminated,
    Invalid,
}


public sealed class PrefetchVolume
{
    public int Index { get; init; }
    public int Offset { get; init; }
    public int EntrySize { get; init; }
    public uint Serial { get; init; }
    public string DevicePath { get; init; } = "";
    public List<string> DirectoryStrings { get; init; } = new();
    public EvidenceLevel Evidence { get; init; }
}


public sealed class PrefetchIdentity
{
    
    public string FilenameExecutable { get; init; } = "";
    
    public uint? FilenameHash { get; init; }
    public IdentityState FilenameExeVsHeader { get; init; } = IdentityState.Unverifiable;
    public IdentityState FilenameHashVsStored { get; init; } = IdentityState.Unverifiable;
    
    public IdentityState StoredHashVsHashString { get; init; } = IdentityState.Unverifiable;
    public string? RenamedNote { get; init; }
}

public enum PrefetchRegionType
{
    Header,
    Metrics,
    TraceChains,
    FileStrings,
    VolumeInformation,
    Unknown,
    TrailingData,
}


public sealed class PrefetchRegion
{
    public PrefetchRegionType Type { get; init; }
    public int Offset { get; init; }
    public int Length { get; init; }
    public double Confidence { get; init; }
}


public sealed class PrefetchRegionOverlap
{
    public PrefetchRegionType First { get; init; }
    public PrefetchRegionType Second { get; init; }
    public int OverlapBytes { get; init; }
}


public sealed class PrefetchUnknownRegion
{
    public int Offset { get; init; }
    public int Length { get; init; }
    public string HexPreview { get; init; } = "";
    public double Entropy { get; init; }
}


public sealed class PrefetchValidation
{
    public PrefetchParseState State { get; init; }
    public List<PrefetchDiagnostic> Errors { get; init; } = new();
    public List<PrefetchDiagnostic> Warnings { get; init; } = new();
    public List<PrefetchDiagnostic> Notes { get; init; } = new();

    public bool Has(PrefetchErrorCode code)
        => Errors.Any(d => d.Code == code) || Warnings.Any(d => d.Code == code) || Notes.Any(d => d.Code == code);
}


public static class SnapshotJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        
        Converters = { new JsonStringEnumConverter() },
    };
}
