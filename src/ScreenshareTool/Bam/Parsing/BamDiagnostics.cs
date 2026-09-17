


public enum BamParseState
{
    Success,
    Partial,
    Invalid,
    Unsupported,
    Recovered,
}


public enum BamErrorCode
{
    None,
    Io,
    Registry,
    Hive,
    Bounds,
    Encoding,
    Sid,
    ValueType,
    ValueLength,
    FileTime,
    Path,
    Layout,
    ControlSet,
    Duplicate,
    Conflict,
    Integrity,
    Recovery,
}


public enum BamLayout
{
    
    LegacyBamUserSettings,
    
    StateBamUserSettings,
    
    LegacyDamUserSettings,
    
    StateDamUserSettings,
    Unknown,
}


public enum ControlSetRole
{
    Current,
    Default,
    LastKnownGood,
    Other,
}


public enum BamDifferenceType
{
    
    TimestampChanged,
    
    PresentOnlyInA,
    
    PresentOnlyInB,
}


public enum BamPathType
{
    NtDevicePath,
    DosPath,
    UncPath,
    Win32Path,
    Unknown,
    Malformed,
}


public enum PathResolutionState
{
    
    Direct,
    
    Derived,
    
    Unresolved,
}


public enum TimestampValidity
{
    Exact,
    Valid,
    Suspicious,
    Invalid,
    Unknown,
}


public enum BamValueState
{
    
    ValidKnownLength,
    
    ShorterThanExpected,
    
    LongerThanExpected,
    
    UnexpectedLength,
}


public enum BamEvidenceLevel
{
    
    Exact,
    
    Validated,
    
    Recovered,
    
    Inferred,
    
    Unknown,
}


public enum EvidenceQuality
{
    Direct,
    Strong,
    Supporting,
    Contextual,
    Weak,
    Unknown,
}


public enum AttributionConfidence
{
    
    High,
    
    Medium,
    
    Low,
}


public readonly record struct BamDiagnostic(BamErrorCode Code, string Message)
{
    public override string ToString() => $"[{Code}] {Message}";
}



public static class BamConfidence
{
    public static double FromState(BamParseState state)
        => state switch
        {
            BamParseState.Success => 99,
            BamParseState.Partial => 70,
            BamParseState.Recovered => 55,
            BamParseState.Unsupported => 25,
            _ => 0,
        };

    public static double Adjust(double baseConfidence, int errorCount, int conflictCount, bool unresolvedPaths)
    {
        double c = baseConfidence;
        c -= Math.Min(20, errorCount * 5);
        c -= Math.Min(10, conflictCount * 3);
        if (unresolvedPaths) c -= 5;
        return Math.Clamp(c, 0, 100);
    }
}
