using System.Text;



public enum CarvedConfidence
{
    Low,
    Medium,
    High,
    VeryHigh,
}


public sealed class CarvedPrefetchCandidate
{
    
    public int Offset { get; init; }
    public PrefetchKind Kind { get; init; }
    public int FormatVersion { get; init; }
    
    public int DeclaredSize { get; init; }
    
    public string ExecutableName { get; init; } = "";
    
    public int Confidence { get; init; }
    public CarvedConfidence Level { get; init; }
    
    public List<PrefetchDiagnostic> Notes { get; init; } = new();
}






internal static class PrefetchCarver
{
    private const int MinCandidateSize = PrefetchCoreParser.MinPfSize;
    private const int MaxDeclaredSize = 16 * 1024 * 1024;

    
    
    public static List<CarvedPrefetchCandidate> Carve(ReadOnlyMemory<byte> data, int maxCandidates = 16)
    {
        var found = new List<CarvedPrefetchCandidate>();
        if (data.Length < MinCandidateSize)
            return found;

        ReadOnlySpan<byte> span = data.Span;
        for (int i = 0; i + 8 <= span.Length; i++)
        {
            
            
            if (span[i + 4] == (byte)'S' && span[i + 5] == (byte)'C' && span[i + 6] == (byte)'C' && span[i + 7] == (byte)'A')
            {
                found.Add(ScoreScca(span, i));
            }
            
            else if (span[i] == (byte)'M' && span[i + 1] == (byte)'A' && span[i + 2] == (byte)'M'
                     && span[i + 3] is >= 1 and <= 4)
            {
                var candidate = ScoreMam(span, i);
                if (candidate is not null)
                    found.Add(candidate);
            }
        }

        
        found.Sort((a, b) =>
        {
            int byOffset = a.Offset.CompareTo(b.Offset);
            return byOffset != 0 ? byOffset : b.Confidence.CompareTo(a.Confidence);
        });

        
        
        
        var merged = new List<CarvedPrefetchCandidate>();
        foreach (var candidate in found)
        {
            if (candidate.Confidence < 40)
                continue;
            int extent = Math.Max(MinCandidateSize, candidate.DeclaredSize > 0 ? candidate.DeclaredSize : MinCandidateSize);
            int existing = merged.FindIndex(m => m.Offset <= candidate.Offset + extent && candidate.Offset <= m.Offset + extent);
            if (existing < 0)
            {
                merged.Add(candidate);
            }
            else if (candidate.Confidence > merged[existing].Confidence)
            {
                merged[existing] = candidate;
            }
            if (merged.Count >= maxCandidates)
                break;
        }
        return merged;
    }

    private static CarvedPrefetchCandidate ScoreScca(ReadOnlySpan<byte> span, int start)
    {
        
        
        int version = ReadI32(span, start + 0x00);
        int declared = ReadI32(span, start + 0x0C);
        string exe = ReadExecutable(span, start + 0x10, 0x3C);
        uint hash = (uint)ReadI32(span, start + 0x4C);

        var notes = new List<PrefetchDiagnostic>();
        int score = 10; 

        if (PrefetchFormatRegistry.IsKnownVersion(version))
        {
            score += 25;
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Version, $"Known format version {version}"));
        }
        else if (version is >= 10 and <= 40)
        {
            score += 8;
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Version, $"Unknown but plausible version {version}"));
        }
        else
        {
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Version, $"Implausible version {version}"));
        }

        int remaining = span.Length - start;
        if (declared >= MinCandidateSize && declared <= MaxDeclaredSize)
        {
            score += 20;
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Integrity, $"Declared size {declared} is plausible"));
            if (declared <= remaining && remaining - declared <= 0x1000)
            {
                score += 10;
                notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Integrity, "Declared size fits the buffer"));
            }
        }
        else
        {
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Integrity, $"Declared size {declared} implausible"));
        }

        if (exe.Length > 0)
        {
            score += 15;
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Header, $"Executable name '{exe}' readable"));
        }
        else
        {
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Header, "Executable name unreadable"));
        }

        
        int metricsCount = ReadI32(span, start + 0x80);
        if (metricsCount is >= 1 and <= 8192)
        {
            score += 10;
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Metrics, $"Metrics count {metricsCount} plausible"));
        }
        else
        {
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Metrics, $"Metrics count {metricsCount} implausible"));
        }

        if (hash != 0)
        {
            score += 5;
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Integrity, $"Stored hash 0x{hash:X8} present"));
        }

        return new CarvedPrefetchCandidate
        {
            Offset = start,
            Kind = PrefetchKind.Scca,
            FormatVersion = version,
            DeclaredSize = declared >= MinCandidateSize && declared <= MaxDeclaredSize ? declared : 0,
            ExecutableName = exe,
            Confidence = score,
            Level = LevelFor(score),
            Notes = notes,
        };
    }

    private static CarvedPrefetchCandidate? ScoreMam(ReadOnlySpan<byte> span, int start)
    {
        
        
        
        
        
        int format = span[start + 3];
        int declared = ReadI32(span, start + 4);
        if (declared < MinCandidateSize || declared > MaxDeclaredSize)
            return null;

        int remaining = span.Length - start - 8;
        int score = 20; 
        var notes = new List<PrefetchDiagnostic>
        {
            new(PrefetchErrorCode.Compression, $"MAM container, format nibble {format}"),
            new(PrefetchErrorCode.Integrity, $"Declared uncompressed size {declared}"),
        };

        if (declared >= MinCandidateSize && declared <= MaxDeclaredSize)
        {
            score += 15;
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Integrity, "Declared size is plausible"));
        }
        if (remaining >= declared / 4) 
        {
            score += 10;
            notes.Add(new PrefetchDiagnostic(PrefetchErrorCode.Integrity, "Compressed payload plausibly fits the buffer"));
        }

        return new CarvedPrefetchCandidate
        {
            Offset = start,
            Kind = PrefetchKind.Mam,
            FormatVersion = 0, 
            DeclaredSize = declared,
            ExecutableName = "",
            Confidence = score,
            Level = LevelFor(score),
            Notes = notes,
        };
    }

    private static CarvedConfidence LevelFor(int score) => score switch
    {
        >= 90 => CarvedConfidence.VeryHigh,
        >= 70 => CarvedConfidence.High,
        >= 45 => CarvedConfidence.Medium,
        _ => CarvedConfidence.Low,
    };

    private static string ReadExecutable(ReadOnlySpan<byte> span, int offset, int fieldBytes)
    {
        if (offset < 0 || offset + 2 > span.Length)
            return "";
        int end = Math.Min(offset + fieldBytes, span.Length - 1);
        for (int i = offset; i + 1 <= end; i += 2)
        {
            if (span[i] == 0 && span[i + 1] == 0)
            {
                end = i;
                break;
            }
        }
        if (end <= offset)
            return "";
        try
        {
            string s = Encoding.Unicode.GetString(span.Slice(offset, end - offset).ToArray()).Trim();
            return s.Length is > 0 and <= 60 && s.Contains('.') ? s : "";
        }
        catch (DecoderFallbackException)
        {
            return "";
        }
    }

    private static int ReadI32(ReadOnlySpan<byte> span, int offset)
    {
        if (offset < 0 || offset + 4 > span.Length)
            return 0;
        return BitConverter.ToInt32(span.Slice(offset, 4));
    }
}
