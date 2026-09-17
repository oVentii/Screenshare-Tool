using System.Text.Json.Serialization;





public enum SignatureStatus
{
    Signed,
    Unsigned,
    NotFound,
    Cheat,
    Fake,
    NotMZ
}




[Flags]
public enum PrefetchRiskFlags
{
    RiskNone = 0,
    RiskUnsigned = 1 << 0,
    RiskFakeSig = 1 << 1,
    RiskCheatSig = 1 << 2,
    RiskYaraMatch = 1 << 3,
    RiskSuspiciousPath = 1 << 4,
    RiskPostLogon = 1 << 5,
    RiskNotMZ = 1 << 6,
    RiskNotFound = 1 << 7,
    RiskHighRunCount = 1 << 8,
    RiskInShimCache = 1 << 9,
    RiskInAmcache = 1 << 10,
    RiskMultiArtifact = 1 << 11,
    RiskYaraDeep = 1 << 12,
    RiskSuspiciousRef = 1 << 13,
    RiskUnsignedRefs = 1 << 14,
    RiskMultiVolume = 1 << 15,
    RiskHiddenFile = 1 << 16,
    RiskSystemFile = 1 << 17,
    RiskReadOnlyFile = 1 << 18,
    RiskDeletedRecovered = 1 << 19,
    RiskDuplicateHash = 1 << 20,
    RiskIntegrityMismatch = 1 << 21,
    RiskCheatRef = 1 << 22,
    RiskArtifactGap = 1 << 23,
    RiskTimestomp = 1 << 24,
    RiskArtifactLeftover = 1 << 25,
    RiskMcPathUnsigned = 1 << 26,
    RiskRenamedPf = 1 << 27,
    RiskInBam = 1 << 28,
    RiskPrefetchHashMismatch = 1 << 29,
    
    
    RiskExecutableLoadedRef = 1 << 30
}






public enum PresenceKind
{
    Present = 0,
    MissingFile = 1,
    UnresolvedPath = 2,
    DeletedPrefetch = 3,
    GhostLeftover = 4
}





public sealed class PrefetchFileRef
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }
    [JsonPropertyName("signatureStatus")]
    public SignatureStatus SignatureStatus { get; set; } = SignatureStatus.NotFound;
    [JsonPropertyName("fileSize")]
    public long? FileSize { get; set; }
    [JsonPropertyName("modified")]
    public string? Modified { get; set; }
    [JsonPropertyName("mftReference")]
    public long MftReference { get; set; }
    
    [JsonPropertyName("fileMetricFlags")]
    public uint FileMetricFlags { get; set; }
    
    
    [JsonPropertyName("loadAsExecutable")]
    public bool LoadAsExecutable { get; set; }
    [JsonPropertyName("suspicious")]
    public bool Suspicious { get; set; }
}




public sealed class PrefetchInfo
{
    [JsonPropertyName("pfFileName")]
    public string? PfFileName { get; set; }
    [JsonPropertyName("pfPath")]
    public string? PfPath { get; set; }
    [JsonPropertyName("version")]
    public int Version { get; set; }
    [JsonPropertyName("versionName")]
    public string? VersionName { get; set; }
    
    [JsonPropertyName("sccaMagic")]
    public int SccaMagic { get; set; }
    [JsonPropertyName("fileSize")]
    public int FileSize { get; set; }
    [JsonPropertyName("runCount")]
    public int RunCount { get; set; }
    
    [JsonPropertyName("headerExeName")]
    public string? HeaderExeName { get; set; }
    
    
    
    [JsonPropertyName("renamedFile")]
    public bool RenamedFile { get; set; }
    
    [JsonPropertyName("prefetchHash")]
    public uint PrefetchHash { get; set; }
    
    
    
    [JsonPropertyName("dedupFingerprint")]
    internal string? DedupFingerprint { get; set; }
    
    
    
    [JsonPropertyName("hashString")]
    public string? HashString { get; set; }
    [JsonPropertyName("mainExecutablePath")]
    public string? MainExecutablePath { get; set; }
    [JsonPropertyName("mainSignatureStatus")]
    public SignatureStatus MainSignatureStatus { get; set; } = SignatureStatus.NotFound;
    
    [JsonPropertyName("signatureDetail")]
    public string? SignatureDetail { get; set; }
    [JsonPropertyName("presenceKind")]
    public PresenceKind PresenceKind { get; set; }
    [JsonPropertyName("fileMissing")]
    public bool FileMissing { get; set; }
    [JsonPropertyName("resolvedFrom")]
    public string? ResolvedFrom { get; set; }
    [JsonPropertyName("lastSeenPath")]
    public string? LastSeenPath { get; set; }
    [JsonPropertyName("lastSeenSource")]
    public string? LastSeenSource { get; set; }
    [JsonPropertyName("lastSeenTime")]
    public string? LastSeenTime { get; set; }
    
    [JsonPropertyName("amcachePublisher")]
    public string? AmcachePublisher { get; set; }

    
    [JsonPropertyName("productName")]
    public string? ProductName { get; set; }
    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }
    [JsonPropertyName("fileDescription")]
    public string? FileDescription { get; set; }
    [JsonPropertyName("fileVersion")]
    public string? FileVersion { get; set; }
    [JsonPropertyName("lastExecutionTimes")]
    public List<string> LastExecutionTimes { get; set; } = new();
    [JsonPropertyName("firstExecUnix")]
    public long FirstExecUnix { get; set; }
    
    
    
    [JsonPropertyName("lastExecUnix")]
    public long LastExecUnix { get; set; }
    [JsonPropertyName("referencedFiles")]
    public List<PrefetchFileRef> ReferencedFiles { get; set; } = new();

    
    [JsonPropertyName("directoryCount")]
    public int DirectoryCount { get; set; }
    [JsonPropertyName("volumeCount")]
    public int VolumeCount { get; set; }
    
    [JsonPropertyName("volumeSerials")]
    public List<uint> VolumeSerials { get; set; } = new();
    
    [JsonPropertyName("volumeDevicePaths")]
    public List<string> VolumeDevicePaths { get; set; } = new();
    
    [JsonPropertyName("directoryNames")]
    public List<string> DirectoryNames { get; set; } = new();
    [JsonPropertyName("unsignedReferenced")]
    public int UnsignedReferenced { get; set; }
    [JsonPropertyName("suspiciousReferenced")]
    public int SuspiciousReferenced { get; set; }
    
    [JsonPropertyName("cheatSignedReferenced")]
    public int CheatSignedReferenced { get; set; }
    [JsonPropertyName("multiVolume")]
    public bool MultiVolume { get; set; }

    
    
    [JsonPropertyName("fileMetricsCount")]
    public int FileMetricsCount { get; set; }
    
    [JsonPropertyName("referencedFileCount")]
    public int ReferencedFileCount { get; set; }
    
    
    [JsonPropertyName("referencedTotal")]
    public int ReferencedTotal { get; set; }
    
    
    [JsonPropertyName("analysisTruncated")]
    public bool AnalysisTruncated { get; set; }
    
    
    [JsonPropertyName("integrityMismatch")]
    public bool IntegrityMismatch { get; set; }

    
    [JsonPropertyName("isHidden")]
    public bool IsHidden { get; set; }
    [JsonPropertyName("isSystem")]
    public bool IsSystem { get; set; }
    [JsonPropertyName("isReadOnly")]
    public bool IsReadOnly { get; set; }
    [JsonPropertyName("wasDeleted")]
    public bool WasDeleted { get; set; }
    [JsonPropertyName("duplicateOf")]
    public string? DuplicateOf { get; set; }
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
    
    
    
    [JsonPropertyName("prefetchHashMismatch")]
    public bool PrefetchHashMismatch { get; set; }

    [JsonPropertyName("riskScore")]
    public int RiskScore { get; set; }
    [JsonPropertyName("riskSummary")]
    public string RiskSummary { get; set; } = "";
    [JsonPropertyName("riskFlags")]
    public PrefetchRiskFlags RiskFlags { get; set; }
    [JsonPropertyName("matchedRules")]
    public List<string> MatchedRules { get; set; } = new();
    [JsonPropertyName("inShimCache")]
    public bool InShimCache { get; set; }
    [JsonPropertyName("inAmcache")]
    public bool InAmcache { get; set; }
    
    [JsonPropertyName("inBam")]
    public bool InBam { get; set; }
    
    [JsonPropertyName("peLastWrite")]
    public string? PeLastWrite { get; set; }
    
    [JsonPropertyName("timestomped")]
    public bool Timestomped { get; set; }
    
    [JsonPropertyName("artifactLeftover")]
    public bool ArtifactLeftover { get; set; }
    
    [JsonPropertyName("unsignedInMinecraftPath")]
    public bool UnsignedInMinecraftPath { get; set; }
    
    
    [JsonPropertyName("isBootPrefetch")]
    public bool IsBootPrefetch { get; set; }
    
    [JsonPropertyName("executableLoadedRefs")]
    public int ExecutableLoadedRefs { get; set; }
    
    [JsonPropertyName("traceChainCount")]
    public int TraceChainCount { get; set; }
    
    
    [JsonPropertyName("totalBlockLoads")]
    public long TotalBlockLoads { get; set; }
    
    
    [JsonPropertyName("maxChainDepth")]
    public int MaxChainDepth { get; set; }
}




public sealed class PrefetchScanResult
{
    [JsonPropertyName("entries")]
    public List<PrefetchInfo> Entries { get; set; } = new();
    [JsonPropertyName("total")]
    public int Total { get; set; }
    [JsonPropertyName("highRisk")]
    public int HighRisk { get; set; }
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
    [JsonPropertyName("shimCacheCount")]
    public int ShimCacheCount { get; set; }
    [JsonPropertyName("amcacheCount")]
    public int AmcacheCount { get; set; }
    
    [JsonPropertyName("bamCount")]
    public int BamCount { get; set; }
    [JsonPropertyName("deletedCount")]
    public int DeletedCount { get; set; }
    [JsonPropertyName("duplicateCount")]
    public int DuplicateCount { get; set; }
    [JsonPropertyName("usnJournalRecreated")]
    public bool UsnJournalRecreated { get; set; }
    [JsonPropertyName("usnJournalEnabled")]
    public bool UsnJournalEnabled { get; set; }
    [JsonPropertyName("usnJournalDisabled")]
    public bool UsnJournalDisabled { get; set; }
    
    
    
    [JsonPropertyName("evidenceGap")]
    public bool EvidenceGap { get; set; }
    [JsonPropertyName("leftoverCount")]
    public int LeftoverCount { get; set; }
    [JsonPropertyName("timestompCount")]
    public int TimestompCount { get; set; }
    [JsonPropertyName("ghostCount")]
    public int GhostCount { get; set; }
    [JsonPropertyName("eventLogCleared")]
    public bool EventLogCleared { get; set; }
    [JsonPropertyName("eventLogClears")]
    public List<EventLogClearHit> EventLogClears { get; set; } = new();
    [JsonPropertyName("ghostArtifacts")]
    public List<GhostArtifact> GhostArtifacts { get; set; } = new();
    [JsonPropertyName("correlationSignals")]
    public List<string> CorrelationSignals { get; set; } = new();
    [JsonPropertyName("recoveredDeleted")]
    public List<PrefetchInfo> RecoveredDeleted { get; set; } = new();
    [JsonPropertyName("ghostEntries")]
    public List<PrefetchInfo> GhostEntries { get; set; } = new();
    [JsonPropertyName("logonTime")]
    public long LogonTime { get; set; }
    [JsonPropertyName("parseFailures")]
    public int ParseFailures { get; set; }
    
    [JsonPropertyName("scanSeconds")]
    public double ScanSeconds { get; set; }
    
    [JsonPropertyName("analysisTruncated")]
    public bool AnalysisTruncated { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    
    
    [JsonPropertyName("requiresAdmin")]
    public bool RequiresAdmin { get; set; }
}



public sealed class GhostArtifact
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";
    [JsonPropertyName("inShimCache")]
    public bool InShimCache { get; set; }
    [JsonPropertyName("inAmcache")]
    public bool InAmcache { get; set; }
    [JsonPropertyName("inBam")]
    public bool InBam { get; set; }
    [JsonPropertyName("fileMissing")]
    public bool FileMissing { get; set; }
    [JsonPropertyName("prefetchMissing")]
    public bool PrefetchMissing { get; set; }
    [JsonPropertyName("signatureStatus")]
    public SignatureStatus SignatureStatus { get; set; }
    [JsonPropertyName("matchedRules")]
    public List<string> MatchedRules { get; set; } = new();
    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }
    [JsonPropertyName("sources")]
    public string Sources { get; set; } = "";
    [JsonPropertyName("riskScore")]
    public int RiskScore { get; set; }
    [JsonPropertyName("lastSeenTime")]
    public string? LastSeenTime { get; set; }
    [JsonPropertyName("lastSeenPath")]
    public string? LastSeenPath { get; set; }
}


public sealed class EventLogClearHit
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";
    [JsonPropertyName("eventId")]
    public int EventId { get; set; }
    [JsonPropertyName("when")]
    public string When { get; set; } = "";
}
