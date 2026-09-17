using System.Text.Json.Serialization;







public sealed class BamEntryInfo
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    [JsonPropertyName("lastExecution")]
    public string LastExecution { get; set; } = "";
    [JsonPropertyName("lastExecutionUnix")]
    public long LastExecutionUnix { get; set; }
    [JsonPropertyName("signature")]
    public SignatureStatus Signature { get; set; } = SignatureStatus.NotFound;
    [JsonPropertyName("signatureDetail")]
    public string SignatureDetail { get; set; } = "";
    [JsonPropertyName("matchedRules")]
    public List<string> MatchedRules { get; set; } = new();
    [JsonPropertyName("fileExists")]
    public bool FileExists { get; set; }
    [JsonPropertyName("inLogonWindow")]
    public bool InLogonWindow { get; set; }
    
    [JsonPropertyName("isSystemEntry")]
    public bool IsSystemEntry { get; set; }
}


public sealed class DeletedBamPath
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    [JsonPropertyName("lastExecution")]
    public string LastExecution { get; set; } = "";
}


public sealed class DeniedRegistryEntry
{
    [JsonPropertyName("keyPath")]
    public string KeyPath { get; set; } = "";
    [JsonPropertyName("permission")]
    public string Permission { get; set; } = "";
}


public sealed class BamScanResult
{
    [JsonPropertyName("entries")]
    public List<BamEntryInfo> Entries { get; set; } = new();
    [JsonPropertyName("deletedPaths")]
    public List<DeletedBamPath> DeletedPaths { get; set; } = new();
    [JsonPropertyName("deniedEntries")]
    public List<DeniedRegistryEntry> DeniedEntries { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }
    [JsonPropertyName("unsigned")]
    public int Unsigned { get; set; }
    [JsonPropertyName("cheatSig")]
    public int CheatSig { get; set; }
    [JsonPropertyName("fakeSig")]
    public int FakeSig { get; set; }
    [JsonPropertyName("notFound")]
    public int NotFound { get; set; }
    [JsonPropertyName("yaraMatch")]
    public int YaraMatch { get; set; }
    [JsonPropertyName("deletedCount")]
    public int DeletedCount { get; set; }
    [JsonPropertyName("deniedCount")]
    public int DeniedCount { get; set; }
    [JsonPropertyName("requiresAdmin")]
    public bool RequiresAdmin { get; set; }
    [JsonPropertyName("deletedReadFailed")]
    public bool DeletedReadFailed { get; set; }
    [JsonPropertyName("deletedReadError")]
    public string DeletedReadError { get; set; } = "";
    [JsonPropertyName("scanSeconds")]
    public double ScanSeconds { get; set; }
}
