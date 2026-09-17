using System.Text.Json;
using System.Text.Json.Serialization;



public sealed class BamSourceRecord
{
    
    public required string ControlSet { get; init; }
    public required ControlSetRole ControlSetRole { get; init; }
    public required BamLayout Layout { get; init; }
    public required string RegistryPath { get; init; }
    public required string UserSidRaw { get; init; }
    
    public required string RawValueName { get; init; }
    public required uint ValueType { get; init; }
    public required int ValueDataLength { get; init; }
    public required string ValueDataHex { get; init; }
    public BamBinaryValue Binary { get; init; } = new()
    {
        DataLength = 0,
        State = BamValueState.UnexpectedLength,
        TimestampValidity = TimestampValidity.Unknown,
    };
    public required BamEvidenceLevel Evidence { get; init; }
    
    public List<BamDiagnostic> Diagnostics { get; } = new();
}


public sealed class BamConflict
{
    public required long TimestampA { get; init; }
    public required string SourceA { get; init; }
    public required long TimestampB { get; init; }
    public required string SourceB { get; init; }
}


public sealed class RecoveredBamPath
{
    
    public required string RawPath { get; init; }
    
    public required int Offset { get; init; }
    
    public required string Source { get; init; }
}


public sealed class BamControlSetDifference
{
    public required string Path { get; init; }
    public required string ControlSetA { get; init; }
    public long? TimestampA { get; init; }
    public required string ControlSetB { get; init; }
    public long? TimestampB { get; init; }
    public required BamDifferenceType Type { get; init; }
}







public sealed class BamArtifact
{
    public required string NormalizedPath { get; init; }
    public BamPath Path { get; init; } = BamPathParser.Parse(null);
    public string? UserSidRaw { get; init; }
    public BamSid? UserSid { get; init; }
    
    public string? UserName { get; init; }
    public AttributionConfidence Attribution { get; init; }
    
    public long? LastExecutionUnix { get; init; }
    public string? LastExecutionUtc { get; init; }
    public TimestampValidity TimestampValidity { get; init; }
    public BamValueState ValueState { get; init; }
    
    public bool? IsWindowsApp { get; init; }
    
    public List<BamSourceRecord> Sources { get; } = new();
    public List<BamConflict> Conflicts { get; } = new();
    public EvidenceQuality Evidence { get; init; }
    public double Confidence { get; init; }

    
    public bool SeenInMultipleControlSets => Sources.Select(s => s.ControlSet).Distinct().Count() > 1;

    public string ToDiagnosticJson() => JsonSerializer.Serialize(this, BamSnapshotJson.Options);
}


public sealed class BamControlSetInfo
{
    public required string Name { get; init; }
    public required ControlSetRole Role { get; init; }
    public int BamEntryCount { get; set; }
}


public sealed class BamStatistics
{
    public int TotalSidKeys { get; set; }
    public int TotalSourceRecords { get; set; }
    public int ValidEntries { get; set; }
    public int InvalidEntries { get; set; }
    public int RecoveredEntries { get; set; }
    public int LegacyLayoutEntries { get; set; }
    public int StateLayoutEntries { get; set; }
    public int DuplicateEntries { get; set; }
    public int ConflictingEntries { get; set; }
    public int InvalidTimestamps { get; set; }
    public int UnresolvedPaths { get; set; }
}


public sealed class BamParseResult
{
    public required BamParseState State { get; set; }
    public double Confidence { get; set; }
    
    public required string SourceName { get; init; }
    public string? SourceHivePath { get; set; }
    public string? SourceHiveSha256 { get; set; }
    public string ParserVersion { get; init; } = BamCoreParser.Version;
    public string OutputSchemaVersion { get; init; } = "1";
    public List<BamControlSetInfo> ControlSets { get; } = new();
    public List<BamArtifact> Artifacts { get; } = new();
    public List<BamControlSetDifference> ControlSetDifferences { get; } = new();
    public List<RecoveredBamPath> RecoveredPaths { get; } = new();
    public List<BamDiagnostic> Errors { get; } = new();
    public List<BamDiagnostic> Warnings { get; } = new();
    public BamStatistics Statistics { get; } = new();

    public string ToDiagnosticJson() => JsonSerializer.Serialize(new
    {
        State,
        Confidence,
        SourceName,
        SourceHivePath,
        SourceHiveSha256,
        ParserVersion,
        OutputSchemaVersion,
        ControlSets,
        Artifacts,
        ControlSetDifferences,
        Errors,
        Warnings,
        Statistics,
    }, BamSnapshotJson.Options);
}


public static class BamSnapshotJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
