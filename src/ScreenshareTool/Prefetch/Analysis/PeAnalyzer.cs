






internal sealed class PeAnalyzer
{
    private readonly byte[] _data;

    private int _peOffset = -1;
    private int _numberOfSections;
    private int _optHeaderSize;
    private int _optMagic;         
    private int _dataDirectoryOffset; 

    public long FileSize => _data.Length;

    public bool IsPe { get; private set; }

    public List<PeSection> Sections { get; } = new();

    public List<(string Dll, string Function)> Imports { get; } = new();

    
    public bool HasEmbeddedSignature { get; private set; }

    public static PeAnalyzer? FromBytes(byte[] data)
    {
        var pe = new PeAnalyzer(data);
        return pe.IsPe ? pe : null;
    }

    private PeAnalyzer(byte[] data)
    {
        _data = data;
        Parse();
    }

    private void Parse()
    {
        if (_data.Length < 0x40) return;
        if (_data[0] != (byte)'M' || _data[1] != (byte)'Z') return;

        int e_lfanew = ReadI32(0x3C);
        if (e_lfanew <= 0 || e_lfanew + 24 > _data.Length) return;
        if (_data[e_lfanew] != (byte)'P' || _data[e_lfanew + 1] != (byte)'E' ||
            _data[e_lfanew + 2] != 0 || _data[e_lfanew + 3] != 0) return;

        _peOffset = e_lfanew;
        int coff = e_lfanew + 4;
        _numberOfSections = ReadU16(coff + 2);
        if (_numberOfSections <= 0 || _numberOfSections > 96) return;

        _optHeaderSize = ReadU16(coff + 16);
        int opt = coff + 20;
        if (opt + 2 > _data.Length) return;

        _optMagic = ReadU16(opt);
        if (_optMagic is not (0x10b or 0x20b)) return;

        int dirCount = 16;
        int dirOffset = opt + (_optMagic == 0x20b ? 112 : 96);
        _dataDirectoryOffset = dirOffset;
        if (dirOffset + dirCount * 8 <= _data.Length)
        {
            int securityRva = ReadI32(dirOffset + 4 * 8);
            int securitySize = ReadI32(dirOffset + 4 * 8 + 4);
            if (securityRva != 0 && securitySize > 0)
            {
                HasEmbeddedSignature = true;
            }
        }

        int sectionOffset = opt + _optHeaderSize;
        for (int i = 0; i < _numberOfSections; i++)
        {
            int s = sectionOffset + i * 40;
            if (s + 40 > _data.Length) break;
            var sec = new PeSection
            {
                Name = ReadAscii(s, 8),
                VirtualSize = ReadU32(s + 8),
                VirtualAddress = ReadU32(s + 12),
                RawDataSize = ReadU32(s + 16),
                RawDataOffset = ReadU32(s + 20),
                Characteristics = ReadU32(s + 36)
            };
            Sections.Add(sec);
        }

        IsPe = true;
        ParseImports();
    }

    private void ParseImports()
    {
        int importRva = ReadI32(_dataDirectoryOffset + 1 * 8);
        if (importRva == 0) return;

        int descRva = importRva;
        int descFile = RvaToOffset(descRva);
        if (descFile < 0) return;

        
        for (int i = 0; i < 64; i++)
        {
            int d = descFile + i * 20;
            if (d + 20 > _data.Length) break;
            int originalThunkRva = ReadI32(d);
            int nameRva = ReadI32(d + 12);
            int firstThunkRva = ReadI32(d + 16);
            if (nameRva == 0 && firstThunkRva == 0 && originalThunkRva == 0) break;

            int nameFile = RvaToOffset(nameRva);
            if (nameFile < 0) continue;
            string dllName = ReadAscii(nameFile, 256);
            if (dllName.Length == 0) continue;

            int thunkRva = originalThunkRva != 0 ? originalThunkRva : firstThunkRva;
            int thunkFile = RvaToOffset(thunkRva);
            if (thunkFile < 0) continue;

            bool is64 = _optMagic == 0x20b;
            ulong ordinalMask = is64 ? 0x8000000000000000UL : 0x80000000UL;
            ulong nameMask = is64 ? 0x7FFFFFFFFFFFFFFFUL : 0x7FFFFFFFUL;
            for (int j = 0; j < 4096; j++)
            {
                int t = thunkFile + j * (is64 ? 8 : 4);
                if (t + (is64 ? 8 : 4) > _data.Length) break;
                ulong value = is64 ? ReadU64(t) : ReadU32(t);
                if (value == 0) break;
                if ((value & ordinalMask) != 0)
                    continue;

                uint hintNameRva = (uint)(value & nameMask);
                int hintNameFile = RvaToOffset((int)hintNameRva);
                if (hintNameFile < 0) continue;
                string func = ReadAscii(hintNameFile + 2, 256);
                if (func.Length == 0) continue;
                string dllKey = dllName.ToLowerInvariant();
                Imports.Add((dllKey, func));
                _importSet.Add(dllKey + "!" + func);
            }
        }
    }

    private int RvaToOffset(int rva)
    {
        foreach (var s in Sections)
        {
            if (rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.RawDataSize))
            {
                long file = (long)s.RawDataOffset + (rva - s.VirtualAddress);
                return file >= 0 && file < _data.Length ? (int)file : -1;
            }
        }
        return rva < _data.Length ? rva : -1;
    }

    
    public bool HasImport(string dllLower, string function)
        => _importSet.Contains(dllLower + "!" + function);

    private readonly HashSet<string> _importSet = new(StringComparer.OrdinalIgnoreCase);

    
    public double Entropy(long offset, long size)
    {
        if(offset<0||size<=0) return 0;
        long end=Math.Min(offset+size,_data.Length); if(offset>=end) return 0;
        const int cap=1_048_576; long total=end-offset; long step=total>cap? total/cap :1;
        int[] cnt=new int[256]; long sampled=0;
        for(long i=offset;i<end;i+=step){ cnt[_data[i]]++; sampled++; }
        if(sampled==0) return 0;
        double e=0; for(int i=0;i<256;i++) if(cnt[i]!=0){ double p=(double)cnt[i]/sampled; e -= p*Math.Log2(p); }
        return e;
    }
    public bool IsPackedSection(PeSection s)=> s.RawDataSize>0 && s.VirtualSize > (long)s.RawDataSize*2 || Entropy(s.RawDataOffset,s.RawDataSize)>7.3;

    
    public double FileEntropy() => Entropy(0, _data.Length);

    public bool HasSectionNamed(string name, StringComparison cmp = StringComparison.Ordinal)
        => Sections.Any(s => string.Equals(s.Name, name, cmp));

    private int ReadU16(int offset) => offset + 2 <= _data.Length
        ? BitConverter.ToUInt16(_data, offset) : 0;
    private uint ReadU32(int offset) => offset + 4 <= _data.Length
        ? BitConverter.ToUInt32(_data, offset) : 0;
    private ulong ReadU64(int offset) => offset + 8 <= _data.Length
        ? BitConverter.ToUInt64(_data, offset) : 0;
    private int ReadI32(int offset) => offset + 4 <= _data.Length
        ? BitConverter.ToInt32(_data, offset) : 0;

    private string ReadAscii(int offset, int maxLen)
    {
        int count = 0;
        while (count < maxLen && offset + count < _data.Length && _data[offset + count] != 0)
            count++;
        return count == 0 ? "" : System.Text.Encoding.ASCII.GetString(_data, offset, count);
    }

    internal sealed class PeSection
    {
        public string Name { get; init; } = "";
        public uint VirtualSize { get; init; }
        public uint VirtualAddress { get; init; }
        public uint RawDataSize { get; init; }
        public uint RawDataOffset { get; init; }
        public uint Characteristics { get; init; }
    }
}
