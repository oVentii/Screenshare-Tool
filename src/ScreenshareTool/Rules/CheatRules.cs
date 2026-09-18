using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// Shared cheat-trace rule engine used identically by the Prefetch and BAM
// modules, so both always report the same Generics.
//
// Rule source (recovered from the authors' builds; rule bodies verified
// against the projects' documented set):
//   prefetch-parser-main + BAM-parser-main "Generic" collections:
//   A/A2/A3, B/B2-B7, C, D, E, F/F2-F7, G/G2-G4, Specifics A/B.
//
// String semantics follow YARA exactly: "/re/i ascii wide" is a
// case-insensitive substring search over raw bytes and over the UTF-16LE
// decoding; plain "str" is case-sensitive ASCII only ("str" wide is
// case-sensitive UTF-16LE only); "$x in (0..filesize)" means anywhere.
// Structural predicates (pe.*, dotnet.*, math.entropy, filesize) mirror the
// YARA 4.x module semantics, including quirks (entry_point is a file offset
// for file scans; F5 indexes pe.sections by stream index; imphash uses table
// order with .dll/.sys/.ocx extension stripping).
internal static class CheatRules
{
    private const int MaxScanBytes = 64 * 1024 * 1024;
    // NT headers can start far into the file (large DOS stubs); the probe
    // must cover e_lfanew + PE signature or valid binaries get rejected.
    private const int HeaderProbeBytes = 65536;
    private const long FilesizeCap = 41943040;

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

    private sealed class StrLit
    {
        public string Text = "";
        public bool IgnoreCase = true;
        public bool Wide = true;
    }

    private static StrLit[] Literals(params string[] patterns)
    {
        var result = new StrLit[patterns.Length];
        for (int i = 0; i < patterns.Length; i++)
            result[i] = new StrLit { Text = Regex.Unescape(patterns[i]) };
        return result;
    }

    private static readonly StrLit[] LiteralsA = Literals(
        "clicker", "autoclick", "clicking", "String Cleaner",
        "double_click", "Jitter Click", "Butterfly Click");

    private static readonly StrLit[] LiteralsSA = Literals(
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
        "rightClickChk\\.BackgroundImage", "UwU Client", "lithiumclient\\.wtf");

    private static readonly StrLit[] LiteralsE = Literals(
        "ObfuscatedByGoliath", "NUITKA_ONEFILE_PARENT",
        "DotNetPatcherPackerAttribute", "DotNetPatcherObfuscatorAttribute",
        "dotNetProtector",
        "Eziriz's \"\\.NET Reactor\"! This assembly won't further work\\.",
        "SmartAssembly\\.HouseOfCards", "Powered by SmartAssembly",
        "ILProtector Signature Begin", "ILProtector Signature End",
        "ILProtector", "ConfuserEx", "ConfusedByAttribute",
        "BabelAttribute", "BabelObfuscatorAttribute", "YanoAttribute",
        "Click to break in debugger!", "ICryptoTransform",
        "PyInstaller", " pyi_win32_utils_to_utf8");

    private static StrLit Plain(string text, bool wide = false)
        => new() { Text = text, IgnoreCase = false, Wide = wide };

    private static readonly string[] SlinkyHashes =
    {
        "5f107e54a521060c04dbd90fd887adf7",
        "aa7790079e5da97cfab7bf84d8bc295b",
        "d1c1dbbd3f23a12ffe26914c72391cde"
    };

    private static readonly string[] B2Names = { ".themida", ".winlice", ".boot" };
    private static readonly string[] B3Names = { ".UPX1", ".UPX0", ".UPX2" };
    private static readonly string[] B4Names = { ".vlizer", ".v-lizer" };
    private static readonly string[] B5Names = { ".idata  ", ".ird0", ".ird1" };
    private static readonly string[] B6Names = { ".MPRESS2", ".MPRESS1", ".MPRESS" };
    private static readonly string[] B7Names =
    {
        ".vmp0", ".vmp1", ".vmp2", ".vmp", ".vroom0", ".vroom1",
        ".urafuck", ".luvsy0", ".luvsy1"
    };

    private sealed class Ctx
    {
        public byte[] Data = Array.Empty<byte>();
        public PeInfo Pe = null!;
        public string Ascii = "";
        public string? WideEven;
        public string? WideOdd;
        public long FileSize;
    }

    public static bool ScanFile(string path, List<RuleHit> hits)
    {
        long length;
        try { length = new FileInfo(path).Length; }
        catch { return false; }
        if (length < 64)
            return false;

        // Header gate: MZ + PE signature (needed by every rule, including the
        // header-only F6/F7 whole-file entropy rules).
        byte[]? header = ReadPrefix(path, HeaderProbeBytes);
        if (header is null || !IsPeHeader(header))
            return false;

        // Files past the buffer cap can only match the whole-file entropy
        // rules (every other rule caps filesize at 40 MB).
        if (length > MaxScanBytes)
            return ScanLargeFile(path, length, hits);

        byte[]? data = ForensicUtil.ReadAllBytesBounded(path, MaxScanBytes);
        if (data is null || data.Length < 64)
            return false;

        var ctx = new Ctx
        {
            Data = data,
            Pe = PeInfo.Parse(data),
            Ascii = Encoding.ASCII.GetString(data),
            FileSize = data.Length
        };

        EvalStrings(ctx, "A", "Generic A", LiteralsA, hits);
        EvalStrings(ctx, "sA", "Specifics A", LiteralsSA, hits);
        EvalStrings(ctx, "E", "Generic E", LiteralsE, hits);
        EvalA2(ctx, hits);
        EvalA3(ctx, hits);
        EvalB(ctx, hits);
        EvalSectionNames(ctx, "B2", "Generic B2", B2Names, hits);
        EvalSectionNames(ctx, "B3", "Generic B3", B3Names, hits);
        EvalSectionNames(ctx, "B4", "Generic B4", B4Names, hits);
        EvalSectionNames(ctx, "B5", "Generic B5", B5Names, hits);
        EvalSectionNames(ctx, "B6", "Generic B6", B6Names, hits);
        EvalSectionNames(ctx, "B7", "Generic B7", B7Names, hits);
        EvalC(ctx, hits);
        EvalD(ctx, hits);
        EvalF(ctx, hits);
        EvalF2(ctx, hits);
        EvalF3(ctx, hits);
        EvalF4(ctx, hits);
        EvalF5(ctx, hits);
        EvalEntropyBand(ctx, hits);
        EvalG(ctx, hits);
        EvalG2(ctx, hits);
        EvalG3(ctx, hits);
        EvalG4(ctx, hits);
        EvalSB(ctx, hits);

        return hits.Count > 0;
    }

    private static bool SizeOk(Ctx ctx, bool strict)
        => strict ? ctx.FileSize < FilesizeCap : ctx.FileSize <= FilesizeCap;

    private static string WideEven(Ctx ctx)
        => ctx.WideEven ??= Encoding.Unicode.GetString(ctx.Data);

    private static string? WideOdd(Ctx ctx)
    {
        if (ctx.WideOdd is not null)
            return ctx.WideOdd;
        if (ctx.Data.Length < 3)
            return null;
        byte[] shifted = new byte[ctx.Data.Length - 1];
        Buffer.BlockCopy(ctx.Data, 1, shifted, 0, shifted.Length);
        if ((shifted.Length & 1) == 1)
            Array.Resize(ref shifted, shifted.Length - 1);
        ctx.WideOdd = Encoding.Unicode.GetString(shifted);
        return ctx.WideOdd;
    }

    // YARA wide strings match at both even and odd file offsets; the whole
    // buffer decoded from zero only covers even alignment, so the odd
    // alignment must be searched separately. Returns the earliest hit.
    private static bool FindWide(Ctx ctx, string lit, bool ignoreCase,
        out int byteOffset, out int count, out string usedText, out int charIndex)
    {
        byteOffset = -1;
        count = 0;
        usedText = "";
        charIndex = -1;
        var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        string even = WideEven(ctx);
        int ei = even.IndexOf(lit, cmp);
        string? odd = WideOdd(ctx);
        int oi = odd is null ? -1 : odd.IndexOf(lit, cmp);

        if (ei < 0 && oi < 0)
            return false;

        if (oi >= 0 && (ei < 0 || oi * 2 + 1 < ei * 2))
        {
            byteOffset = oi * 2 + 1;
            usedText = odd!;
            charIndex = oi;
        }
        else
        {
            byteOffset = ei * 2;
            usedText = even;
            charIndex = ei;
        }
        count = CountOccurrences(usedText, lit, cmp);
        return true;
    }

    private static int CountOccurrences(string haystack, string lit, StringComparison cmp)
    {
        int count = 0;
        int pos = 0;
        while (count < 10000 && (pos = haystack.IndexOf(lit, pos, cmp)) >= 0)
        {
            count++;
            pos += lit.Length;
        }
        return count;
    }

    private static void AddHit(List<RuleHit> hits, string id, string name,
        string sample, string encoding, long offset, int count, string context)
    {
        hits.Add(new RuleHit
        {
            Id = id,
            DisplayName = name,
            Sample = sample.Length > 80 ? sample.Substring(0, 80) : sample,
            Encoding = encoding,
            Offset = offset,
            Count = count,
            Context = context
        });
    }

    private static bool FindFirst(string haystack, string lit, bool ignoreCase, out int index, out int count)
    {
        var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        index = haystack.IndexOf(lit, cmp);
        if (index < 0)
        {
            count = 0;
            return false;
        }
        count = 0;
        int pos = 0;
        while (count < 10000 && (pos = haystack.IndexOf(lit, pos, cmp)) >= 0)
        {
            count++;
            pos += lit.Length;
        }
        return true;
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

    private static void EvalStrings(Ctx ctx, string id, string name, StrLit[] lits, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: false))
            return;
        foreach (var lit in lits)
        {
            if (FindFirst(ctx.Ascii, lit.Text, lit.IgnoreCase, out int idx, out int count))
            {
                AddHit(hits, id, name, lit.Text, "ascii", idx, count,
                    ExtractContext(ctx.Ascii, idx, lit.Text.Length));
                return;
            }
            if (lit.Wide && FindWide(ctx, lit.Text, lit.IgnoreCase, out int boff, out int wcount, out string wtext, out int widx))
            {
                AddHit(hits, id, name, lit.Text, "wide", boff, wcount,
                    ExtractContext(wtext, widx, lit.Text.Length));
                return;
            }
        }
    }

    private static void EvalA2(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || ctx.Pe.HasDotNet || !SizeOk(ctx, strict: false))
            return;
        string? mouse = null;
        foreach (string f in new[] { "mouse_event", "SendInput", "SetCursorPos", "SendMessageA", "SendMessageW", "PostMessageA", "PostMessageW" })
        {
            if (ctx.Pe.HasImport("user32.dll", f)) { mouse = f; break; }
        }
        if (mouse is null) return;
        string? key = null;
        foreach (string f in new[] { "GetAsyncKeyState", "GetKeyState" })
        {
            if (ctx.Pe.HasImport("user32.dll", f)) { key = f; break; }
        }
        if (key is null) return;
        AddHit(hits, "A2", "Generic A2", $"user32.dll!{mouse} + user32.dll!{key}", "imports", 0, 1, "");
    }

    private static void EvalA3(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !ctx.Pe.HasDotNet || !SizeOk(ctx, strict: false))
            return;
        string[] group1 =
        {
            "mouse_event", "SendInput", "SetCursorPos", "SendMessageA",
            "SendMessageW", "PostMessageA", "PostMessageW"
        };
        string[] group2 = { "GetAsyncKeyState", "GetKeyState" };
        string? g1 = null, g2 = null;
        foreach (string f in group1)
        {
            if (ctx.Ascii.Contains(f, StringComparison.Ordinal)) { g1 = f; break; }
        }
        if (g1 is null) return;
        foreach (string f in group2)
        {
            if (ctx.Ascii.Contains(f, StringComparison.Ordinal)) { g2 = f; break; }
        }
        if (g2 is null) return;
        AddHit(hits, "A3", "Generic A3", $"{g1} + {g2}", "ascii", ctx.Ascii.IndexOf(g1, StringComparison.Ordinal), 1, "");
    }

    private static void EvalB(Ctx ctx, List<RuleHit> hits)
    {
        var pe = ctx.Pe;
        if (!pe.IsPe || !SizeOk(ctx, strict: false))
            return;
        if (pe.NumberOfSections < 4)
            return;
        if (pe.Sections.Count < 4)
            return;
        if (pe.Sections[1].Name != ".rsrc" || pe.Sections[2].Name != ".idata")
            return;
        var s0 = pe.Sections[0];
        double? ent = pe.Entropy(s0.RawOff, s0.RawSize);
        if (ent is null || ent < 7.5)
            return;
        if (pe.EntryPointOffset < 0)
            return;
        bool inside = false;
        for (int i = 3; i < pe.Sections.Count; i++)
        {
            var s = pe.Sections[i];
            if ((ulong)pe.EntryPointOffset >= s.VAddr &&
                (ulong)pe.EntryPointOffset < (ulong)s.VAddr + s.VSize)
            {
                inside = true;
                break;
            }
        }
        if (!inside) return;
        AddHit(hits, "B", "Generic B",
            $"entropy(.text-like[0])={ent:F2}, entry in section", "", pe.EntryPointOffset, 1, "");
    }

    private static void EvalSectionNames(Ctx ctx, string id, string name, string[] wanted, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: false))
            return;
        if (ctx.Pe.NumberOfSections <= 0)
            return;
        int matches = 0;
        string first = "";
        foreach (var s in ctx.Pe.Sections)
        {
            foreach (string w in wanted)
            {
                if (s.Name == w)
                {
                    matches++;
                    if (first.Length == 0) first = w;
                }
            }
        }
        if (matches > 0)
            AddHit(hits, id, name, first, "sections", 0, matches, "");
    }

    private static void EvalC(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: false))
            return;
        if (!FindFirst(ctx.Ascii, "<Module>", ignoreCase: false, out _, out _))
            return;
        bool wideOk = FindWide(ctx, "Program will be terminated.", ignoreCase: false,
            out _, out _, out _, out _);
        bool adjacent = false;
        for (int i = 0; i + 1 < ctx.Pe.Sections.Count; i++)
        {
            if (ctx.Pe.Sections[i].Name == ctx.Pe.Sections[i + 1].Name)
            {
                adjacent = true;
                break;
            }
        }
        if (!wideOk && !adjacent)
            return;
        AddHit(hits, "C", "Generic C", wideOk ? "Program will be terminated." : "adjacent duplicate sections", wideOk ? "wide" : "sections", 0, 1, "");
    }

    private static void EvalD(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: false))
            return;
        if (ctx.Pe.DirCount <= 14 || ctx.Pe.DirRva[14] == 0)
            return;
        if (ctx.Pe.NumberOfSections < 3)
            return;
        if (!FindFirst(ctx.Ascii, "SuppressIldasmAttribute", ignoreCase: false, out int idx, out int count))
            return;
        AddHit(hits, "D", "Generic D", "SuppressIldasmAttribute", "ascii", idx, count,
            ExtractContext(ctx.Ascii, idx, "SuppressIldasmAttribute".Length));
    }

    private static void EvalF(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: false))
            return;
        int idx = ctx.Pe.SectionIndex(".text");
        if (idx < 0 || idx >= ctx.Pe.Sections.Count)
            return;
        var s = ctx.Pe.Sections[idx];
        if (s.RawSize == 0)
            return;
        double? ent = ctx.Pe.Entropy(s.RawOff, s.RawSize);
        if (ent is null || ent <= 6.5)
            return;
        AddHit(hits, "F", "Generic F", $"entropy(.text)={ent:F2}", "", 0, 1, "");
    }

    private static void EvalF2(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: false))
            return;
        if (ctx.Pe.NumberOfSections < 8 || ctx.Pe.Sections.Count <= 7)
            return;
        var s = ctx.Pe.Sections[7];
        double? ent = ctx.Pe.Entropy(s.RawOff, s.RawSize);
        if (ent is null || ent <= 7.5)
            return;
        AddHit(hits, "F2", "Generic F2", $"entropy(sections[7])={ent:F2}", "", 0, 1, "");
    }

    private static void EvalF3(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: false))
            return;
        foreach (var s in ctx.Pe.Sections)
        {
            if (s.RawSize == 0)
                continue;
            double? ent = ctx.Pe.Entropy(s.RawOff, s.RawSize);
            if (ent is not null && ent > 7.5)
            {
                AddHit(hits, "F3", "Generic F3", $"entropy({s.Name})={ent:F2}", "", 0, 1, "");
                return;
            }
        }
    }

    private static void EvalF4(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: false))
            return;
        if (ctx.Pe.OverlaySize == 0)
            return;
        double? ent = ctx.Pe.Entropy(ctx.Pe.OverlayOffset, ctx.Pe.OverlaySize);
        if (ent is null || ent <= 7.5)
            return;
        AddHit(hits, "F4", "Generic F4", $"entropy(overlay)={ent:F2}", "", (long)ctx.Pe.OverlayOffset, 1, "");
    }

    private static void EvalF5(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.HasDotNet || !SizeOk(ctx, strict: false))
            return;
        for (int i = 0; i < ctx.Pe.DotNetStreams.Count; i++)
        {
            var st = ctx.Pe.DotNetStreams[i];
            if (st.Name != "#EncryptedStrings" && st.Name != "#StrEnc")
                continue;
            if (st.Size >= 1000)
                continue;
            if (i >= ctx.Pe.Sections.Count)
                continue;
            var sec = ctx.Pe.Sections[i];
            double? ent = ctx.Pe.Entropy(sec.RawOff, st.Size);
            if (ent is not null && ent > 7.0)
            {
                AddHit(hits, "F5", "Generic F5", $"{st.Name} ({st.Size}b, entropy={ent:F2})", "", 0, 1, "");
                return;
            }
        }
    }

    private static void EvalEntropyBand(Ctx ctx, List<RuleHit> hits)
    {
        double? ent = ctx.Pe.WholeFileEntropy();
        if (ent is null) return;
        if (ent >= 7.1 && ent < 7.62)
            AddHit(hits, "F6", "Generic F6", $"entropy={ent:F2}", "", 0, 1, "");
        else if (ent >= 7.62)
            AddHit(hits, "F7", "Generic F7", $"entropy={ent:F2}", "", 0, 1, "");
    }

    private static bool ImportGroup(Ctx ctx, (string Dll, string[] Funcs)[] groups, int need, out string sample)
    {
        sample = "";
        int hit = 0;
        var sb = new StringBuilder();
        foreach (var g in groups)
        {
            string? found = null;
            foreach (string f in g.Funcs)
            {
                if (ctx.Pe.HasImport(g.Dll, f)) { found = f; break; }
            }
            if (found is null) continue;
            hit++;
            if (sb.Length > 0) sb.Append(" + ");
            sb.Append(g.Dll).Append('!').Append(found);
            if (hit >= need) break;
        }
        if (hit < need) return false;
        sample = sb.ToString();
        return true;
    }

    private static void EvalG(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: true))
            return;
        if (!ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "CreateRemoteThread", "CreateThread" }), ("ntdll.dll", new[] { "NtCreateThreadEx" }), ("kernel32.dll", new[] { "QueueUserAPC" }) }, 1, out _))
            return;
        if (!ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "WriteProcessMemory" }), ("ntdll.dll", new[] { "NtWriteVirtualMemory" }) }, 1, out _))
            return;
        if (!ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "OpenProcess", "VirtualAllocEx", "VirtualProtectEx", "OpenThread" }) }, 1, out _))
            return;
        AddHit(hits, "G", "Generic G", "remote thread + remote write + open/alloc", "imports", 0, 1, "");
    }

    private static void EvalG2(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: true))
            return;
        bool hollow =
            ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "CreateProcessA", "CreateProcessW" }) }, 1, out _) &&
            ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "WriteProcessMemory" }), ("ntdll.dll", new[] { "NtWriteVirtualMemory" }) }, 1, out _) &&
            ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "SetThreadContext", "GetThreadContext", "ResumeThread" }) }, 1, out _) &&
            ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "VirtualAllocEx", "VirtualProtect", "VirtualProtectEx" }) }, 1, out _);
        bool mapped =
            ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "CreateFileMappingA", "CreateFileMappingW" }), ("ntdll.dll", new[] { "NtCreateSection" }) }, 1, out _) &&
            ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "MapViewOfFile" }), ("ntdll.dll", new[] { "NtMapViewOfSection" }) }, 1, out _) &&
            ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "CreateProcessA", "CreateProcessW" }) }, 1, out _);
        if (!hollow && !mapped)
            return;
        AddHit(hits, "G2", "Generic G2", hollow ? "process hollowing imports" : "section mapping imports", "imports", 0, 1, "");
    }

    private static void EvalG3(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: true))
            return;
        if (!ImportGroup(ctx, new[] { ("user32.dll", new[] { "SetWindowsHookExA", "SetWindowsHookExW" }) }, 1, out _))
            return;
        if (!ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "LoadLibraryA", "LoadLibraryW", "LoadLibraryExA", "LoadLibraryExW" }) }, 1, out _))
            return;
        AddHit(hits, "G3", "Generic G3", "SetWindowsHookEx + LoadLibrary", "imports", 0, 1, "");
    }

    private static void EvalG4(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: true))
            return;
        bool snapshot =
            ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "CreateToolhelp32Snapshot" }) }, 1, out _) &&
            ImportGroup(ctx, new[] { ("kernel32.dll", new[] { "Process32FirstW", "Process32NextW" }) }, 1, out _);
        bool shell =
            ImportGroup(ctx, new[] { ("SHELL32.dll", new[] { "ShellExecuteA", "ShellExecuteW" }) }, 1, out _);
        if (!snapshot && !shell)
            return;
        AddHit(hits, "G4", "Generic G4", snapshot ? "toolhelp process enumeration" : "ShellExecute", "imports", 0, 1, "");
    }

    private static void EvalSB(Ctx ctx, List<RuleHit> hits)
    {
        if (!ctx.Pe.IsPe || !SizeOk(ctx, strict: false))
            return;
        foreach (var s in ctx.Pe.Sections)
        {
            if (s.Name == ".entropy")
            {
                AddHit(hits, "sB", "Specifics B", ".entropy section", "sections", 0, 1, "");
                return;
            }
        }
        string yara = ctx.Pe.ImpHashYara;
        string sorted = ctx.Pe.ImpHashSorted;
        foreach (string h in SlinkyHashes)
        {
            if ((!string.IsNullOrEmpty(yara) && string.Equals(yara, h, StringComparison.Ordinal)) ||
                (!string.IsNullOrEmpty(sorted) && string.Equals(sorted, h, StringComparison.Ordinal)))
            {
                AddHit(hits, "sB", "Specifics B", "imphash=" + h, "", 0, 1, "");
                return;
            }
        }
    }

    private static bool ScanLargeFile(string path, long length, List<RuleHit> hits)
    {
        // Past the buffer cap only the whole-file entropy bands can match.
        double? ent = StreamEntropy(path);
        if (ent is null) return false;
        if (ent >= 7.1 && ent < 7.62)
            AddHit(hits, "F6", "Generic F6", $"entropy={ent:F2}", "", 0, 1, "");
        else if (ent >= 7.62)
            AddHit(hits, "F7", "Generic F7", $"entropy={ent:F2}", "", 0, 1, "");
        else
            return false;
        return true;
    }

    private static double? StreamEntropy(string path)
    {
        try
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
            if (fs.Length <= 0) return null;
            long[] counts = new long[256];
            long total = 0;
            byte[] chunk = new byte[1048576];
            int n;
            while ((n = fs.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += n;
                for (int i = 0; i < n; i++)
                    counts[chunk[i]]++;
            }
            if (total == 0) return null;
            double entropy = 0.0;
            foreach (long c in counts)
            {
                if (c == 0) continue;
                double p = (double)c / total;
                entropy -= p * Math.Log2(p);
            }
            return entropy;
        }
        catch
        {
            return null;
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
