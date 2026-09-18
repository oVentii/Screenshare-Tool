using System.Security.Cryptography;
using System.Text;

internal sealed class PeInfo
{
    public bool IsPe;
    public long FileSize;
    public bool Is64;
    public ulong ImageBase;
    public long EntryPointOffset = -1;
    public int NumberOfSections;
    public ushort Characteristics;
    public int NumberOfRvaAndSizes;
    public readonly List<Section> Sections = new();
    public readonly List<Import> Imports = new();
    public readonly uint[] DirRva = new uint[16];
    public readonly uint[] DirSize = new uint[16];
    public int DirCount;
    public bool HasDotNet;
    public readonly List<DotNetStream> DotNetStreams = new();
    public ulong OverlayOffset;
    public ulong OverlaySize;
    public string ImpHashYara = "";
    public string ImpHashSorted = "";

    public sealed class Section
    {
        public string Name = "";
        public uint VAddr;
        public uint VSize;
        public uint RawSize;
        public uint RawOff;
    }

    public sealed class Import
    {
        public string Dll = "";
        public string? Func;
    }

    public sealed class DotNetStream
    {
        public string Name = "";
        public uint Size;
    }

    private readonly byte[] _d;

    private PeInfo(byte[] data)
    {
        _d = data;
        FileSize = data.Length;
    }

    public static PeInfo Parse(byte[] data)
    {
        var pe = new PeInfo(data);
        pe.ParseHeader();
        if (pe.IsPe)
        {
            pe.ParseSections();
            pe.ParseDirectories();
            pe.ParseImports();
            pe.ParseDotNet();
            pe.ComputeOverlay();
            pe.ComputeImpHashes();
        }
        return pe;
    }

    private bool U16(long off, out ushort v)
    {
        v = 0;
        if (off < 0 || off + 2 > _d.Length) return false;
        v = BitConverter.ToUInt16(_d, (int)off);
        return true;
    }

    private bool U32(long off, out uint v)
    {
        v = 0;
        if (off < 0 || off + 4 > _d.Length) return false;
        v = BitConverter.ToUInt32(_d, (int)off);
        return true;
    }

    private bool I32(long off, out int v)
    {
        v = 0;
        if (off < 0 || off + 4 > _d.Length) return false;
        v = BitConverter.ToInt32(_d, (int)off);
        return true;
    }

    private long RvaToOffset(uint rva)
    {
        foreach (var s in Sections)
        {
            ulong span = Math.Max(s.VSize, s.RawSize);
            if (span == 0) continue;
            if (rva >= s.VAddr && (ulong)rva < (ulong)s.VAddr + span)
            {
                ulong off = (ulong)s.RawOff + (rva - s.VAddr);
                if (off > (ulong)_d.Length) return -1;
                return (long)off;
            }
        }
        return -1;
    }

    private string ReadAsciiZ(long off, int maxLen)
    {
        if (off < 0 || off >= _d.Length) return "";
        int len = 0;
        while (len < maxLen && off + len < _d.Length && _d[off + len] != 0)
            len++;
        return Encoding.ASCII.GetString(_d, (int)off, len);
    }

    private void ParseHeader()
    {
        if (_d.Length < 64) return;
        if (_d[0] != (byte)'M' || _d[1] != (byte)'Z') return;
        if (!I32(0x3C, out int peOff) || peOff < 0) return;
        long pe = peOff;
        if (pe + 6 > _d.Length) return;
        if (_d[pe] != (byte)'P' || _d[pe + 1] != (byte)'E' || _d[pe + 2] != 0 || _d[pe + 3] != 0)
            return;
        if (!U16(pe + 4 + 16, out ushort optSize)) return;
        if (!U16(pe + 4 + 2, out ushort numSec)) return;
        if (!U16(pe + 4 + 18, out ushort chars)) return;
        Characteristics = chars;
        long opt = pe + 4 + 20;
        if (opt + 2 > _d.Length) return;
        ushort magic = BitConverter.ToUInt16(_d, (int)opt);
        if (magic != 0x10b && magic != 0x20b) return;
        Is64 = magic == 0x20b;
        long secBase = opt + optSize;
        if (secBase < opt || secBase > _d.Length) return;

        IsPe = true;
        NumberOfSections = numSec;

        if (!U32(pe + 4 + 20 + (Is64 ? 108 : 92), out uint numRva)) numRva = 0;
        NumberOfRvaAndSizes = (int)Math.Min(numRva, 256);

        if (Is64)
        {
            if (U32(pe + 4 + 20 + 16, out uint ep)) EntryPointRva = ep;
            if (U32(pe + 4 + 20 + 24, out uint b0) && U32(pe + 4 + 20 + 28, out uint b1))
                ImageBase = ((ulong)b1 << 32) | b0;
        }
        else
        {
            if (U32(pe + 4 + 20 + 16, out uint ep)) EntryPointRva = ep;
            if (U32(pe + 4 + 20 + 28, out uint b)) ImageBase = b;
        }
    }

    private uint EntryPointRva;

    private void ParseSections()
    {
        if (!I32(0x3C, out int peOff)) return;
        long pe = peOff;
        if (!U16(pe + 4 + 16, out ushort optSize)) return;
        if (!U16(pe + 4 + 2, out ushort numSec)) return;
        long opt = pe + 4 + 20;
        long secBase = opt + optSize;
        int count = Math.Min((int)numSec, 96);
        for (int i = 0; i < count; i++)
        {
            long s = secBase + i * 40L;
            if (s + 40 > _d.Length) break;
            int nameLen = 0;
            while (nameLen < 8 && _d[s + nameLen] != 0) nameLen++;
            var sec = new Section
            {
                Name = Encoding.ASCII.GetString(_d, (int)s, nameLen),
                VSize = BitConverter.ToUInt32(_d, (int)s + 8),
                VAddr = BitConverter.ToUInt32(_d, (int)s + 12),
                RawSize = BitConverter.ToUInt32(_d, (int)s + 16),
                RawOff = BitConverter.ToUInt32(_d, (int)s + 20)
            };
            Sections.Add(sec);
        }

        if (U32(pe + 4 + 20 + 16, out uint epRva))
        {
            long off = RvaToOffset(epRva);
            EntryPointOffset = off;
        }
    }

    private void ParseDirectories()
    {
        if (!I32(0x3C, out int peOff)) return;
        long pe = peOff;
        if (!U16(pe + 4 + 16, out ushort optSize)) return;
        long opt = pe + 4 + 20;
        if (!U16(opt, out ushort magic)) return;
        long dirBase = opt + (magic == 0x20b ? 112 : 96);
        int n = Math.Min(NumberOfRvaAndSizes, 16);
        for (int i = 0; i < n; i++)
        {
            long d = dirBase + i * 8L;
            if (d + 8 > _d.Length) break;
            DirRva[i] = BitConverter.ToUInt32(_d, (int)d);
            DirSize[i] = BitConverter.ToUInt32(_d, (int)d + 4);
            DirCount = i + 1;
        }
    }

    private void ParseImports()
    {
        if (DirCount <= 1 || DirRva[1] == 0) return;
        long table = RvaToOffset(DirRva[1]);
        if (table < 0) return;
        int totalFuncs = 0;
        for (int d = 0; d < 2000; d++)
        {
            long e = table + d * 20L;
            if (e + 20 > _d.Length) break;
            uint oft = BitConverter.ToUInt32(_d, (int)e);
            uint ft = BitConverter.ToUInt32(_d, (int)e + 16);
            uint nameRva = BitConverter.ToUInt32(_d, (int)e + 12);
            if (oft == 0 && ft == 0 && nameRva == 0 &&
                BitConverter.ToUInt32(_d, (int)e + 4) == 0 &&
                BitConverter.ToUInt32(_d, (int)e + 8) == 0)
                break;
            long nameOff = RvaToOffset(nameRva);
            if (nameOff < 0) continue;
            string dll = ReadAsciiZ(nameOff, 512);
            if (dll.Length == 0) continue;
            long thunkRva = oft != 0 ? oft : ft;
            ParseThunks(thunkRva, dll, ref totalFuncs);
            if (totalFuncs > 100000) break;
        }
    }

    private void ParseThunks(long thunkRva, string dll, ref int totalFuncs)
    {
        long off = RvaToOffset((uint)(thunkRva & 0xFFFFFFFF));
        if (off < 0) return;
        int step = Is64 ? 8 : 4;
        ulong flag = Is64 ? 0x8000000000000000ul : 0x80000000ul;
        for (int t = 0; t < 100000; t++)
        {
            long p = off + (long)t * step;
            if (p + step > _d.Length) break;
            ulong val = step == 8 ? BitConverter.ToUInt64(_d, (int)p) : BitConverter.ToUInt32(_d, (int)p);
            if (val == 0) break;
            totalFuncs++;
            if (totalFuncs > 100000) break;
            if ((val & flag) != 0)
            {
                Imports.Add(new Import { Dll = dll });
            }
            else
            {
                long nameOff = RvaToOffset((uint)(val & 0xFFFFFFFF));
                string func = nameOff >= 0 && nameOff + 2 <= _d.Length
                    ? ReadAsciiZ(nameOff + 2, 512)
                    : "";
                if (func.Length == 0) continue;
                Imports.Add(new Import { Dll = dll, Func = func });
            }
        }
    }

    private void ParseDotNet()
    {
        HasDotNet = false;
        if (DirCount <= 14 || DirRva[14] == 0) return;
        long cliOff = RvaToOffset(DirRva[14]);
        if (cliOff < 0 || cliOff + 72 > _d.Length) return;
        if (BitConverter.ToUInt32(_d, (int)cliOff) != 72) return;

        if (!U32(cliOff + 8, out uint mdRva)) return;
        long mdRoot = RvaToOffset(mdRva);
        if (mdRoot < 0 || mdRoot + 16 > _d.Length) return;
        if (BitConverter.ToUInt32(_d, (int)mdRoot) != 0x424A5342) return;
        if (!U32(mdRoot + 12, out uint mdLen)) return;
        if (mdLen == 0 || mdLen > 255 || mdLen % 4 != 0) return;
        if (mdRoot + 16 + mdLen > _d.Length) return;

        if (Is64)
        {
            if (NumberOfRvaAndSizes < 14) return;
        }
        else if ((Characteristics & 0x2000) == 0)
        {
            // 32-bit EXE stub check: entry point starts with FF 25.
            long epOff = EntryPointOffset;
            if (epOff < 0 || epOff + 2 > _d.Length) return;
            if (_d[epOff] != 0xFF || _d[epOff + 1] != 0x25) return;
        }

        HasDotNet = true;

        long p = mdRoot + 16 + mdLen + 2;
        if (p + 2 > _d.Length) return;
        int streams = _d[p] | (_d[p + 1] << 8);
        p += 2;
        for (int i = 0; i < streams && i < 64; i++)
        {
            if (p + 8 > _d.Length) break;
            uint sOff = BitConverter.ToUInt32(_d, (int)p);
            uint sSize = BitConverter.ToUInt32(_d, (int)p + 4);
            int nStart = (int)p + 8;
            int nEnd = nStart;
            while (nEnd < _d.Length && _d[nEnd] != 0 && nEnd - nStart < 64) nEnd++;
            string name = Encoding.ASCII.GetString(_d, nStart, nEnd - nStart);
            long abs = mdRoot + sOff;
            if (abs < 0 || abs > _d.Length) { p = (nEnd + 4) & ~3; continue; }
            DotNetStreams.Add(new DotNetStream { Name = name, Size = sSize });
            p = (nEnd + 4) & ~3;
            if (p <= nStart) break;
        }
    }

    private void ComputeOverlay()
    {
        OverlayOffset = 0;
        OverlaySize = 0;
        uint bestOff = 0;
        uint bestSize = 0;
        bool any = false;
        foreach (var s in Sections)
        {
            if (!any || s.RawOff > bestOff || (s.RawOff == bestOff && s.RawSize > bestSize))
            {
                bestOff = s.RawOff;
                bestSize = s.RawSize;
                any = true;
            }
        }
        if (!any) return;
        ulong end = (ulong)bestOff + bestSize;
        if (end > 0 && (ulong)_d.Length > end)
        {
            OverlayOffset = end;
            OverlaySize = (ulong)_d.Length - end;
        }
    }

    private static string LowerAscii(string s)
    {
        char[] c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++)
        {
            if (c[i] >= 'A' && c[i] <= 'Z')
                c[i] = (char)(c[i] + 32);
        }
        return new string(c);
    }

    private static string StripExt(string dll)
    {
        int dot = dll.IndexOf('.');
        if (dot >= 0)
        {
            string ext = dll.Substring(dot, Math.Min(4, dll.Length - dot));
            if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".sys", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".ocx", StringComparison.OrdinalIgnoreCase))
                return dll.Substring(0, dot);
        }
        return dll;
    }

    private void ComputeImpHashes()
    {
        var yara = new StringBuilder();
        bool first = true;
        foreach (var imp in Imports)
        {
            if (imp.Func is null) continue;
            if (!first) yara.Append(',');
            yara.Append(LowerAscii(StripExt(imp.Dll)));
            yara.Append('.');
            yara.Append(LowerAscii(imp.Func));
            first = false;
        }
        if (yara.Length > 0)
            ImpHashYara = Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(yara.ToString()))).ToLowerInvariant();

        var sorted = new List<string>();
        foreach (var imp in Imports)
        {
            if (imp.Func is null) continue;
            sorted.Add(LowerAscii(StripExt(imp.Dll)) + "." + LowerAscii(imp.Func));
        }
        if (sorted.Count > 0)
        {
            sorted.Sort(StringComparer.Ordinal);
            ImpHashSorted = Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(string.Join(",", sorted)))).ToLowerInvariant();
        }
    }

    public bool HasImport(string dll, string func)
    {
        foreach (var imp in Imports)
        {
            if (imp.Func is null) continue;
            if (string.Equals(imp.Dll, dll, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(imp.Func, func, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public int SectionIndex(string name)
    {
        for (int i = 0; i < Sections.Count; i++)
        {
            if (string.Equals(Sections[i].Name, name, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    public double? Entropy(ulong offset, ulong size)
    {
        if (size == 0)
        {
            if (offset > (ulong)_d.Length) return null;
            return 0.0;
        }
        if (offset >= (ulong)_d.Length) return null;
        if (offset + size > (ulong)_d.Length || offset + size < offset) return null;
        long[] counts = new long[256];
        int o = (int)offset;
        int n = (int)size;
        for (int i = 0; i < n; i++)
            counts[_d[o + i]]++;
        double entropy = 0.0;
        for (int i = 0; i < 256; i++)
        {
            if (counts[i] == 0) continue;
            double p = (double)counts[i] / n;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    public double? WholeFileEntropy()
    {
        if (_d.Length == 0) return null;
        return Entropy(0, (ulong)_d.Length);
    }
}
