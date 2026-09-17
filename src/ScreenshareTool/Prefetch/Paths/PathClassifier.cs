using System.IO;


internal static class PathClassifier
{
    
    
    
    private static readonly string[] MinecraftMarkers =
    {
        @"\.minecraft\", @"\minecraft\", @"\lunarclient\", @"\.lunarclient\",
        @"\badlion\", @"\feather\", @"\.feather\", @"\labymod\",
        @"\prismlauncher\", @"\multimc\", @"\polymc\", @"\modrinthapp\",
        @"\atlauncher\", @"\gdlauncher\", @"\tlauncher\", @"\hmcl\",
        @"\curseforge\minecraft", @"\essential\", @"\fabric\", @"\forge\",
        @"\versions\"
    };

    public static bool IsMinecraftRelatedPath(string path)
    {
        if (string.IsNullOrEmpty(path) || ForensicUtil.IsUnresolved(path))
            return false;

        string lower = path.ToLowerInvariant();
        return MinecraftMarkers.Any(m => lower.Contains(m, StringComparison.Ordinal));
    }

    
    
    private static readonly string[] SuspiciousDirs =
    {
        @"\temp\", @"\tmp\", @"\appdata\local\temp\", @"\downloads\",
        @"\users\public\", @"\$recycle.bin\", @"\windows\temp\",
        @"\perflogs\", @"\appdata\roaming\", @"\programdata\"
    };

    public static bool IsSuspiciousExecutablePath(string path)
    {
        if (string.IsNullOrEmpty(path) || ForensicUtil.IsUnresolved(path))
            return false;

        string lower = path.ToLowerInvariant();
        if (SuspiciousDirs.Any(d => lower.Contains(d, StringComparison.Ordinal)))
            return true;

        
        
        if (lower.Contains(@"\users\", StringComparison.Ordinal) &&
            !lower.Contains(@"\appdata\", StringComparison.Ordinal) &&
            !lower.Contains(@"\documents\", StringComparison.Ordinal) &&
            !lower.Contains(@"\desktop\", StringComparison.Ordinal) &&
            !lower.Contains(@"\downloads\", StringComparison.Ordinal))
        {
            int pos = lower.IndexOf(@"\users\", StringComparison.Ordinal);
            string rest = lower[(pos + 7)..];
            int slash = rest.IndexOf('\\');
            if (slash >= 0)
            {
                rest = rest[(slash + 1)..];
                if (!rest.Contains('\\'))
                    return true;
            }
        }
        return false;
    }

    
    private static readonly string[] CheatNameMarkers =
    {
        "inject", "loader", "mapper", "kdmapper", "cheat", "aimbot", "hack",
        "macro", "bypass", "keygen", "crack", "unhook", "spoofer",
        "triggerbot", "wallhack"
    };

    public static bool IsSuspiciousReferenced(string path, SignatureStatus s)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        string lower = path.ToLowerInvariant();
        string name = Path.GetFileNameWithoutExtension(lower);

        if (s is SignatureStatus.Cheat or SignatureStatus.Fake)
            return true;

        bool untrusted = s is SignatureStatus.Unsigned or SignatureStatus.NotMZ
            or SignatureStatus.Cheat or SignatureStatus.Fake or SignatureStatus.NotFound;
        if (!untrusted)
            return false;

        if (IsMinecraftRelatedPath(lower) &&
            s is SignatureStatus.Unsigned or SignatureStatus.NotMZ or SignatureStatus.Cheat or SignatureStatus.Fake)
            return true;

        bool inSystem = lower.Contains(@"\windows\system32\", StringComparison.Ordinal)
                     || lower.Contains(@"\windows\syswow64\", StringComparison.Ordinal)
                     || lower.Contains(@"\windows\winsxs\", StringComparison.Ordinal)
                     || lower.Contains(@"\windows\microsoft.net\", StringComparison.Ordinal);
        bool bad = lower.Contains(@"\temp\", StringComparison.Ordinal)
                || lower.Contains(@"\downloads\", StringComparison.Ordinal)
                || lower.Contains(@"\tmp\", StringComparison.Ordinal)
                || lower.Contains(@"\$recycle.bin\", StringComparison.Ordinal);
        bool suspiciousName = CheatNameMarkers.Any(n => name.Contains(n, StringComparison.Ordinal));

        return (suspiciousName && !inSystem) || bad;
    }
}
