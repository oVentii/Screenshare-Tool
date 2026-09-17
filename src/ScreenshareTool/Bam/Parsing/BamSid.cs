using System.Globalization;



public sealed class BamSid
{
    
    public required string Raw { get; init; }
    public required bool Valid { get; init; }
    
    public int Revision { get; init; }
    
    public int SubAuthorityCount { get; init; }
    public ulong IdentifierAuthority { get; init; }
    
    public IReadOnlyList<uint> SubAuthorities { get; init; } = Array.Empty<uint>();
    
    public uint Rid => SubAuthorities.Count > 0 ? SubAuthorities[^1] : 0;
    
    public bool IsWellKnown { get; init; }
    
    public bool IsMachineUser { get; init; }
    public string? WellKnownName { get; init; }
    
    public string? InvalidReason { get; init; }

    public override string ToString() => Raw;
}


internal static class BamSidParser
{
    
    public static BamSid Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Invalid(raw ?? "", "Empty SID");

        string s = raw.Trim();
        if (!s.StartsWith("S-", StringComparison.OrdinalIgnoreCase))
            return Invalid(s, "Does not start with S-");

        string[] parts = s[2..].Split('-');
        if (parts.Length < 3) 
            return Invalid(s, "Too few components");

        
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int revision) || revision != 1)
            return Invalid(s, $"Implausible revision '{parts[0]}'");

        ulong authority;
        if (!TryParseAuthority(parts[1], out authority))
            return Invalid(s, $"Malformed identifier authority '{parts[1]}'");

        var subAuthorities = new List<uint>(parts.Length - 2);
        for (int i = 2; i < parts.Length; i++)
        {
            if (!uint.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out uint rid))
                return Invalid(s, $"Malformed sub-authority '{parts[i]}'");
            subAuthorities.Add(rid);
        }
        if (subAuthorities.Count == 0)
            return Invalid(s, "No sub-authorities");

        bool isWellKnown = WellKnown.TryGetValue(s, out string? wellKnownName);
        bool isMachineUser = !isWellKnown
                             && authority == 5
                             && subAuthorities.Count >= 4
                             && subAuthorities[0] == 21      
                             && subAuthorities[^1] >= 1000;  

        return new BamSid
        {
            Raw = raw,
            Valid = true,
            Revision = revision,
            SubAuthorityCount = subAuthorities.Count,
            IdentifierAuthority = authority,
            SubAuthorities = subAuthorities,
            IsWellKnown = isWellKnown,
            WellKnownName = wellKnownName,
            IsMachineUser = isMachineUser,
        };
    }

    private static BamSid Invalid(string raw, string reason)
        => new() { Raw = raw, Valid = false, InvalidReason = reason };

    
    private static bool TryParseAuthority(string text, out ulong value)
    {
        value = 0;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    
    private static readonly Dictionary<string, string> WellKnown = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S-1-0-0"] = "Nobody",
        ["S-1-1-0"] = "Everyone",
        ["S-1-2-0"] = "Local",
        ["S-1-3-0"] = "Creator Owner",
        ["S-1-4"] = "Non-unique Authority",
        ["S-1-5-18"] = "SYSTEM",
        ["S-1-5-19"] = "LOCAL SERVICE",
        ["S-1-5-20"] = "NETWORK SERVICE",
        ["S-1-5-32-544"] = "Administrators",
        ["S-1-5-32-545"] = "Users",
        ["S-1-5-32-546"] = "Guests",
        ["S-1-5-32-555"] = "Remote Desktop Users",
        ["S-1-5-11"] = "Authenticated Users",
        ["S-1-5-113"] = "Local account",
        ["S-1-16-12288"] = "High integrity",
    };
}
