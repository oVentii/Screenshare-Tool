using System.IO;
using System.Text;
using System.Text.RegularExpressions;

internal static class PrefetchRules
{
    private const int MaxScanBytes = 64 * 1024 * 1024;
    private const int HeaderProbeBytes = 8192;

    private sealed class Rule
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string[] Literals { get; init; } = Array.Empty<string>();
    }

    public sealed class RuleHit
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Sample { get; init; } = "";
        public string Encoding { get; init; } = "";
        public long Offset { get; init; }
        public int Count { get; init; }
        public string Context { get; init; } = "";
    }

    private static string[] Literals(params string[] patterns)
    {
        var result = new string[patterns.Length];
        for (int i = 0; i < patterns.Length; i++)
            result[i] = Regex.Unescape(patterns[i]);
        return result;
    }

    private static readonly Rule[] Rules =
    {
        new()
        {
            Id = "A",
            DisplayName = "Generic A",
            Literals = Literals(
                "clicker", "autoclick", "clicking", "String Cleaner",
                "double_click", "Jitter Click", "Butterfly Click")
        },
        new()
        {
            Id = "sA",
            DisplayName = "Specifics A",
            Literals = Literals(
                "Exodus\\.codes", "slinky\\.gg", "slinkyhook\\.dll",
                "slinky_library\\.dll", "\\[!\\] Failed to find Vape jar",
                "Vape Launcher", "vape\\.gg",
                "C:\\\\Users\\\\PC\\\\Desktop\\\\Cleaner-main\\\\obj\\\\x64\\\\Release\\\\WindowsFormsApp3\\.pdb",
                "discord\\.gg\\/advantages", "String cleaner",
                "Open Minecraft, then try again\\.", "The clicker code was done by Nightbot\\. I skidded it :\\)",
                "PE injector", "name=\"SparkCrack\\.exe\"", "starlight v1\\.0",
                "Sapphire LITE Clicker", "Striker\\.exe", "Cracked by Kangaroo",
                "Monolith Lite", "B\\.fagg0t0", "B\\.fag0", "\\.\\fag1",
                "dream-injector",
                "C:\\\\Users\\\\Daniel\\\\Desktop\\\\client-top\\\\x64\\\\Release\\\\top-external\\.pdb",
                "C:\\\\Users\\\\Daniel\\\\Desktop\\\\client-top\\\\x64\\\\Release\\\\top-internal\\.pdb",
                "UNICORN CLIENT", "Adding delay to Minecraft",
                "rightClickChk\\.BackgroundImage", "UwU Client", "lithiumclient\\.wtf")
        }
    };

    public static bool ScanFile(string path, List<RuleHit> hits)
    {
        // Fast path: most non-executables are rejected from the first 8 KB
        // without ever reading (or decoding) the whole file.
        long length;
        try { length = new FileInfo(path).Length; }
        catch { return false; }
        if (length < 64 || length > MaxScanBytes)
            return false;

        byte[]? data;
        if (length <= HeaderProbeBytes)
        {
            data = ForensicUtil.ReadAllBytesBounded(path, HeaderProbeBytes);
            if (data is null || !IsPeHeader(data))
                return false;
        }
        else
        {
            byte[]? header = ReadPrefix(path, HeaderProbeBytes);
            if (header is null || !IsPeHeader(header))
                return false;
            data = ForensicUtil.ReadAllBytesBounded(path, MaxScanBytes);
            if (data is null || data.Length < 64)
                return false;
        }

        bool any = false;

        string ascii = Encoding.ASCII.GetString(data);
        foreach (var rule in Rules)
        {
            if (MatchLiterals(ascii, rule, 1, out RuleHit? hit) && hit is not null)
            {
                hits.Add(hit);
                any = true;
            }
        }

        // Wide (UTF-16LE) decode touches every byte pair; only pay for it
        // when at least one rule is still unmatched.
        if (hits.Count < Rules.Length)
        {
            string wide = Encoding.Unicode.GetString(data);
            foreach (var rule in Rules)
            {
                if (hits.Any(h => h.Id == rule.Id))
                    continue;
                if (MatchLiterals(wide, rule, 2, out RuleHit? hit) && hit is not null)
                {
                    hits.Add(hit);
                    any = true;
                }
            }
        }

        return any;
    }

    private static bool MatchLiterals(string haystack, Rule rule, int charSize, out RuleHit? hit)
    {
        hit = null;
        foreach (string lit in rule.Literals)
        {
            if (lit.Length == 0)
                continue;
            int first = haystack.IndexOf(lit, StringComparison.OrdinalIgnoreCase);
            if (first < 0)
                continue;

            int count = 0;
            int pos = 0;
            while (count < 10000 &&
                   (pos = haystack.IndexOf(lit, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                pos += lit.Length;
            }

            string sample = lit.Length > 80 ? lit.Substring(0, 80) : lit;
            hit = new RuleHit
            {
                Id = rule.Id,
                DisplayName = rule.DisplayName,
                Sample = sample,
                Encoding = charSize == 2 ? "wide" : "ascii",
                Offset = (long)first * charSize,
                Count = count,
                Context = ExtractContext(haystack, first, lit.Length)
            };
            return true;
        }
        return false;
    }

    private static string ExtractContext(string haystack, int index, int length)
    {
        try
        {
            int start = Math.Max(0, index - 40);
            int end = Math.Min(haystack.Length, index + length + 40);
            var sb = new StringBuilder(end - start);
            for (int i = start; i < end; i++)
            {
                char c = haystack[i];
                sb.Append(c < 0x20 || c == 0x7F ? '·' : c);
            }
            string s = sb.ToString();
            return s.Length > 96 ? s.Substring(0, 96) : s;
        }
        catch
        {
            return "";
        }
    }

    private static byte[]? ReadPrefix(string path, int count)
    {
        try
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (fs.Length < 64)
                return null;
            byte[] buf = new byte[Math.Min(count, (int)Math.Min(fs.Length, int.MaxValue))];
            int read = 0;
            while (read < buf.Length)
            {
                int n = fs.Read(buf, read, buf.Length - read);
                if (n == 0) break;
                read += n;
            }
            if (read < 64)
                return null;
            if (read < buf.Length)
                Array.Resize(ref buf, read);
            return buf;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPeHeader(byte[] data)
    {
        if (data.Length < 64)
            return false;
        if (data[0] != (byte)'M' || data[1] != (byte)'Z')
            return false;
        int peOff = BitConverter.ToInt32(data, 0x3C);
        if (peOff < 0 || peOff + 6 > data.Length)
            return false;
        return data[peOff] == (byte)'P' && data[peOff + 1] == (byte)'E'
            && data[peOff + 2] == 0 && data[peOff + 3] == 0;
    }
}
