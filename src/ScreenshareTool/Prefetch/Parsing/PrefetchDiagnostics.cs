

public enum PrefetchKind
{
    
    NotPrefetch,
    
    Scca,
    
    Mam,
}







public enum PrefetchParseState
{
    Success,
    Partial,
    Invalid,
    Corrupted,
    Unsupported,
    UnknownVersion,
}


public enum PrefetchErrorCode
{
    None,
    Io,
    Compression,
    Header,
    Bounds,
    Encoding,
    Version,
    Metrics,
    TraceChain,
    Volume,
    Timestamp,
    Integrity,
    Unsupported,
    Identity,
}







public enum EvidenceLevel
{
    Exact,
    Validated,
    Recovered,
    Inferred,
    Unknown,
}


public enum IdentityState
{
    Match,
    Mismatch,
    Ambiguous,
    Unverifiable,
}


public enum SupportLevel
{
    
    Full,
    
    Partial,
    
    Unknown,
}


public readonly record struct PrefetchDiagnostic(PrefetchErrorCode Code, string Message)
{
    public override string ToString() => $"[{Code}] {Message}";
}



public static class PrefetchConfidence
{
    public static double FromState(PrefetchParseState state)
        => state switch
        {
            PrefetchParseState.Success => 99,
            PrefetchParseState.Partial => 70,
            PrefetchParseState.Corrupted => 40,
            PrefetchParseState.UnknownVersion => 25,
            PrefetchParseState.Unsupported => 20,
            _ => 0,
        };

    
    
    
    
    public static double Adjust(double baseConfidence, int unknownRegionCount, bool sizeMismatch, bool integrityMismatch)
    {
        double c = baseConfidence;
        c -= Math.Min(20, unknownRegionCount * 5);
        if (sizeMismatch) c -= 10;
        if (integrityMismatch) c -= 5;
        return Math.Clamp(c, 0, 100);
    }
}
