using System.Reflection;
using System.Text;







internal static class PrefetchHash
{
    private const uint Seed = 314159u;
    private const uint Multiplier = 37u;

    
    
    
    
    
    
    public static uint Compute(string path)
    {
        if (string.IsNullOrEmpty(path))
            return 0;
        byte[] utf16 = Encoding.Unicode.GetBytes(path.ToUpperInvariant());
        uint hash = Seed;
        foreach (byte b in utf16)
            hash = unchecked(hash * Multiplier + b);
        return hash;
    }

    
    
    
    
    
    public static bool IsStoredHashValid(uint stored, string? hashString, int version)
    {
        if (stored == 0)
            return false; 
        if (version is not (30 or 31) || string.IsNullOrWhiteSpace(hashString))
            return false; 
        if (!hashString.StartsWith(@"\DEVICE\", StringComparison.OrdinalIgnoreCase))
            return false; 
        return Compute(hashString) == stored;
    }

    
    
    
    
    
    
    public static bool IsMismatched(uint stored, string? hashString, int version)
    {
        if (stored == 0)
            return false;
        if (version is not (30 or 31) || string.IsNullOrWhiteSpace(hashString))
            return false;
        if (!hashString.StartsWith(@"\DEVICE\", StringComparison.OrdinalIgnoreCase))
            return false;
        return Compute(hashString) != stored;
    }
}
