using System.Globalization;
using System.IO;



public sealed class PrefetchFilename
{
    public required string FileName { get; init; }
    
    public required string ExecutableName { get; init; }
    
    public uint? FilenameHash { get; init; }
    
    public required bool IsBootPrefetch { get; init; }
}

internal static class PrefetchFilenameParser
{
    
    
    
    
    
    
    
    
    public static PrefetchFilename Parse(string? fileName)
    {
        string name = fileName ?? "";
        if (name.EndsWith(".pf", StringComparison.OrdinalIgnoreCase))
            name = name[..^3];

        bool boot = LooksBootPrefetch(name);
        string exe = ExtractExecutable(name);
        uint? hash = ExtractHash(name);

        return new PrefetchFilename
        {
            FileName = fileName ?? "",
            ExecutableName = exe,
            FilenameHash = hash,
            IsBootPrefetch = boot,
        };
    }

    
    
    
    
    
    public static bool LooksBootPrefetch(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (name.EndsWith(".pf", StringComparison.OrdinalIgnoreCase))
            name = name[..^3];
        if (name.StartsWith("NTOSBOOT-", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("Op-", StringComparison.OrdinalIgnoreCase))
        {
            int dash2 = name.LastIndexOf('-');
            if (dash2 > 0 && IsHex(name.AsSpan(dash2 + 1)))
            {
                string head = name[..dash2];
                int dash1 = head.LastIndexOf('-');
                if (dash1 > 0 && IsHex(head.AsSpan(dash1 + 1)))
                    return true;
            }
        }
        return false;
    }

    
    
    
    
    
    
    public static string ExtractExecutable(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";

        bool stripped = false;
        string filename = name;
        for (int pass = 0; pass < 2; pass++)
        {
            int dash = filename.LastIndexOf('-');
            if (dash <= 0)
                break;
            string tail = filename[(dash + 1)..];
            if (tail.Length == 8 && IsHex(tail))
            {
                filename = filename[..dash];
                stripped = true;
            }
            else
            {
                break;
            }
        }

        
        
        if (stripped)
            return filename;

        int first = filename.IndexOf('-');
        return first >= 0 ? filename[..first] : filename;
    }

    
    public static uint? ExtractHash(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        int dash = name.LastIndexOf('-');
        if (dash <= 0 || dash + 9 != name.Length)
            return null;
        string tail = name[(dash + 1)..];
        if (tail.Length != 8 || !IsHex(tail))
            return null;
        return uint.TryParse(tail, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v : null;
    }

    
    
    
    
    public static string NormalizeExeBaseName(string? name)
    {
        string n = Path.GetFileNameWithoutExtension(name ?? "");
        var sb = new System.Text.StringBuilder(n.Length);
        foreach (char c in n)
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    
    
    
    
    
    public static bool IsRenamed(string filenameExe, string? headerExe)
    {
        if (string.IsNullOrWhiteSpace(headerExe))
            return false;
        string a = NormalizeExeBaseName(filenameExe);
        string b = NormalizeExeBaseName(headerExe);
        if (a.Length == 0 || b.Length == 0 || a == b)
            return false;

        
        
        string rawA = Path.GetFileNameWithoutExtension(filenameExe ?? "") ?? "";
        if (rawA.StartsWith("op-", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeExeBaseName(rawA[3..]), b, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    private static bool IsHex(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
            if (!Uri.IsHexDigit(c))
                return false;
        return true;
    }
}
