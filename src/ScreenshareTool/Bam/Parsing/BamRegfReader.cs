using System.Text;



public sealed class RegfKey
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    
    public long LastWriteFileTime { get; init; }
}


public sealed class RegfValue
{
    public required string Name { get; init; }
    public required uint Type { get; init; }
    public required byte[] Data { get; init; }
    
    public int DataOffset { get; init; }
}









internal sealed class BamRegfReader
{
    private readonly byte[] _data;
    private readonly int _rootOffset;
    private readonly List<BamDiagnostic> _diagnostics = new();

    private const int BaseBlockSize = 0x1000;
    private const int HbinHeaderSize = 0x20;
    private const uint MaxBigDataSegments = 1024;
    private const int MaxValueNameBytes = 32 * 1024;

    public IReadOnlyList<BamDiagnostic> Diagnostics => _diagnostics;

    private BamRegfReader(byte[] data, int rootOffset)
    {
        _data = data;
        _rootOffset = rootOffset;
    }

    public static BamRegfReader? TryOpen(byte[] data)
    {
        if (data is null || data.Length < BaseBlockSize + HbinHeaderSize)
            return null;
        if (data[0] != (byte)'r' || data[1] != (byte)'e' || data[2] != (byte)'g' || data[3] != (byte)'f')
            return null;

        int rootOffset = (int)ReadU32(data, 0x24);
        if (!IsValidCellOffset(rootOffset, data.Length))
            return null;

        var reader = new BamRegfReader(data, rootOffset);
        var root = reader.ReadNk(rootOffset);
        if (root is null || !root.Value.NkIsRoot)
            return null;
        return reader;
    }

    
    public int Size => _data.Length;

    public string? RootName => ReadNk(_rootOffset)?.Name;

    
    
    
    public RegfKey? GetKey(string path)
    {
        int? offset = ResolveOffset(path);
        if (offset is null)
            return null;
        var node = ReadNk(offset.Value);
        if (node is null)
            return null;
        return new RegfKey
        {
            Name = node.Value.Name,
            Path = path,
            LastWriteFileTime = node.Value.LastWriteFileTime,
        };
    }

    public IEnumerable<string> EnumerateSubKeys(string path)
    {
        int? offset = ResolveOffset(path);
        if (offset is null)
            yield break;
        var nk = ReadNk(offset.Value);
        if (nk is null)
            yield break;
        foreach (int childOffset in EnumerateSubKeyOffsets(nk.Value))
        {
            var child = ReadNk(childOffset);
            if (child is not null)
                yield return child.Value.Name;
        }
    }

    public IReadOnlyList<RegfValue> EnumerateValues(string path)
    {
        var values = new List<RegfValue>();
        int? resolved = ResolveOffset(path);
        if (resolved is null)
            return values;
        int offset = resolved.Value;
        var nk = ReadNk(offset);
        if (nk is null)
            return values;

        int count = (int)nk.Value.NumberOfValues;
        int listOffset = (int)nk.Value.ValuesListOffset;
        if (count <= 0 || listOffset <= 0 || !TryCell(listOffset, out _, out int dataStart, out int dataLen))
            return values;

        int entries = Math.Min(count, dataLen / 4);
        for (int i = 0; i < entries; i++)
        {
            int vkOffset = (int)ReadU32At(dataStart + i * 4);
            var vk = ReadVk(vkOffset);
            if (vk is null)
                continue;
            byte[]? data = ReadValueData(vk.Value);
            if (data is null)
            {
                _diagnostics.Add(new BamDiagnostic(BamErrorCode.Hive,
                    $"Value '{vk.Value.Name}' of '{path}' has unreadable data"));
                data = Array.Empty<byte>();
            }
            values.Add(new RegfValue
            {
                Name = vk.Value.Name,
                Type = vk.Value.Type,
                Data = data,
                DataOffset = vk.Value.DataOffset,
            });
        }
        return values;
    }

    
    
    
    
    
    public IEnumerable<(int Offset, byte[] Payload)> EnumerateFreeCells(int maxPayloadBytes = 4096)
    {
        int pos = BaseBlockSize;
        while (pos + HbinHeaderSize <= _data.Length)
        {
            if (_data[pos] == (byte)'h' && _data[pos + 1] == (byte)'b' && _data[pos + 2] == (byte)'b' && _data[pos + 3] == (byte)'i')
            {
                int hbinSize = (int)ReadU32(_data, pos + 8);
                if (hbinSize < HbinHeaderSize || pos + hbinSize > _data.Length)
                    break;
                int cell = pos + HbinHeaderSize;
                int end = pos + hbinSize;
                while (cell + 4 <= end)
                {
                    int size = (int)ReadI32(_data, cell);
                    if (size == 0 || Math.Abs(size) < 4 || cell + Math.Abs(size) > end)
                        break;
                    if (size < 0) 
                    {
                        int payloadLen = Math.Min(-size - 4, maxPayloadBytes);
                        if (payloadLen > 0)
                        {
                            var payload = new byte[payloadLen];
                            Array.Copy(_data, cell + 4, payload, 0, payloadLen);
                            yield return (cell + 4, payload);
                        }
                    }
                    cell += Math.Abs(size);
                }
                pos += hbinSize;
            }
            else
            {
                
                int next = FindNextHbin(pos);
                if (next < 0)
                    break;
                pos = next;
            }
        }
    }

    private int FindNextHbin(int from)
    {
        for (int i = from + 4; i + 4 <= _data.Length; i += 4)
        {
            if (_data[i] == (byte)'h' && _data[i + 1] == (byte)'b' && _data[i + 2] == (byte)'b' && _data[i + 3] == (byte)'i')
                return i;
        }
        return -1;
    }

    private int? ResolveOffset(string path)
    {
        if (string.IsNullOrEmpty(path))
            return _rootOffset;
        int offset = _rootOffset;
        foreach (string part in path.Split('\\'))
        {
            if (part.Length == 0)
                return null;
            var node = ReadNk(offset);
            if (node is null)
                return null;
            int child = FindSubKey(node.Value, part);
            if (child < 0)
                return null;
            offset = child;
        }
        return offset;
    }

    
    
    

    private readonly record struct NkInfo(string Name, long LastWriteFileTime, bool NkIsRoot,
        uint NumberOfSubKeys, uint SubKeysListOffset, uint NumberOfValues, uint ValuesListOffset);

    private readonly record struct VkInfo(string Name, uint Type, int DataOffset, uint DataSize, bool DataInline);

    private NkInfo? ReadNk(int offset)
    {
        if (!TryCell(offset, out _, out int dataStart, out int dataLen) || dataLen < 0x4E)
            return null;
        if (!IsAscii(dataStart, "nk", 2))
            return null;
        long lastWrite = ReadI64At(dataStart + 4);
        uint subCount = ReadU32At(dataStart + 0x14);
        uint subList = ReadU32At(dataStart + 0x1C);
        uint valueCount = ReadU32At(dataStart + 0x24);
        uint valueList = ReadU32At(dataStart + 0x28);
        int nameLen = ReadU16At(dataStart + 0x48);
        bool isRoot = subCount > 0 || valueCount > 0 || subList != 0 || valueList != 0 || offset == _rootOffset;
        if (nameLen > 0 && nameLen <= dataLen - 0x4C)
        {
            string name = DecodeUtf16(dataStart + 0x4C, nameLen);
            return new NkInfo(name, lastWrite, isRoot, subCount, subList, valueCount, valueList);
        }
        return new NkInfo("", lastWrite, isRoot, subCount, subList, valueCount, valueList);
    }

    private VkInfo? ReadVk(int offset)
    {
        if (!TryCell(offset, out _, out int dataStart, out int dataLen) || dataLen < 0x14)
            return null;
        if (!IsAscii(dataStart, "vk", 2))
            return null;
        int nameLen = ReadU16At(dataStart + 2);
        uint rawSize = ReadU32At(dataStart + 4);
        uint dataOffsetField = ReadU32At(dataStart + 8);
        uint type = ReadU32At(dataStart + 0x0C);

        bool inline = (rawSize & 0x80000000u) != 0 || rawSize <= 4;
        uint dataSize = rawSize & 0x7FFFFFFFu;
        if (!inline && dataSize <= 4)
            inline = true;

        string name = "";
        if (nameLen > 0 && nameLen <= MaxValueNameBytes && nameLen <= dataLen - 0x14)
            name = DecodeUtf16(dataStart + 0x14, nameLen);

        return new VkInfo(name, type, (int)dataOffsetField, dataSize, inline);
    }

    private IEnumerable<int> EnumerateSubKeyOffsets(NkInfo nk)
    {
        if (nk.NumberOfSubKeys == 0 || nk.SubKeysListOffset == 0)
            yield break;
        if (!TryCell((int)nk.SubKeysListOffset, out _, out int dataStart, out int dataLen))
            yield break;
        if (dataLen < 4)
            yield break;

        
        
        string sig = Ascii(dataStart, 2);
        switch (sig)
        {
            case "lf":
            case "lh":
            {
                int count = ReadU16At(dataStart + 2);
                int max = Math.Min(count, (dataLen - 4) / 8);
                for (int i = 0; i < max; i++)
                    yield return (int)ReadU32At(dataStart + 4 + i * 8);
                break;
            }
            case "li":
            {
                int count = ReadU16At(dataStart + 2);
                int max = Math.Min(count, (dataLen - 4) / 4);
                for (int i = 0; i < max; i++)
                    yield return (int)ReadU32At(dataStart + 4 + i * 4);
                break;
            }
            case "ri":
            {
                int count = ReadU16At(dataStart + 2);
                int max = Math.Min(count, (dataLen - 4) / 4);
                for (int i = 0; i < max; i++)
                {
                    int subList = (int)ReadU32At(dataStart + 4 + i * 4);
                    if (!TryCell(subList, out _, out int subStart, out int subLen) || subLen < 4)
                        continue;
                    string subSig = Ascii(subStart, 2);
                    if (subSig is "lf" or "lh")
                    {
                        int c = ReadU16At(subStart + 2);
                        int m = Math.Min(c, (subLen - 4) / 8);
                        for (int j = 0; j < m; j++)
                            yield return (int)ReadU32At(subStart + 4 + j * 8);
                    }
                    else if (subSig == "li")
                    {
                        int c = ReadU16At(subStart + 2);
                        int m = Math.Min(c, (subLen - 4) / 4);
                        for (int j = 0; j < m; j++)
                            yield return (int)ReadU32At(subStart + 4 + j * 4);
                    }
                }
                break;
            }
        }
    }

    private int FindSubKey(NkInfo nk, string name)
    {
        foreach (int childOffset in EnumerateSubKeyOffsets(nk))
        {
            var child = ReadNk(childOffset);
            if (child is not null && string.Equals(child.Value.Name, name, StringComparison.OrdinalIgnoreCase))
                return childOffset;
        }
        return -1;
    }

    private byte[]? ReadValueData(VkInfo vk)
    {
        uint size = vk.DataSize;
        if (size == 0)
            return Array.Empty<byte>();
        if (size > 256 * 1024 * 1024)
            return null; 

        if (vk.DataInline)
        {
            
            byte[] data = new byte[Math.Min(size, 4)];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(vk.DataOffset >> (8 * i));
            return data;
        }

        if (size > 16344) 
        {
            return ReadBigData((int)vk.DataOffset, (int)size);
        }

        if (!TryCell(vk.DataOffset, out _, out int dataStart, out int dataLen))
            return null;
        if (dataLen < (int)size)
            return null;
        byte[] result = new byte[size];
        Array.Copy(_data, dataStart, result, 0, (int)size);
        return result;
    }

    private byte[]? ReadBigData(int dbOffset, int totalSize)
    {
        if (!TryCell(dbOffset, out _, out int dataStart, out int dataLen) || dataLen < 4)
            return null;
        if (!IsAscii(dataStart, "db", 2))
            return null;
        int segments = ReadU16At(dataStart + 2);
        if (segments <= 0 || segments > MaxBigDataSegments)
            return null;
        if (dataLen < 4 + segments * 8)
            return null;

        var output = new byte[totalSize];
        int written = 0;
        for (int i = 0; i < segments && written < totalSize; i++)
        {
            int segOffset = (int)ReadU32At(dataStart + 4 + i * 8);
            int segSize = (int)ReadU32At(dataStart + 4 + i * 8 + 4);
            if (segSize <= 0 || !TryCell(segOffset, out _, out int segStart, out int segLen))
                return null;
            int take = Math.Min(segSize, Math.Min(segLen, totalSize - written));
            Array.Copy(_data, segStart, output, written, take);
            written += take;
        }
        return written == totalSize ? output : null;
    }

    
    
    

    private bool TryCell(int offset, out int cellSize, out int dataStart, out int dataLen)
    {
        cellSize = 0;
        dataStart = 0;
        dataLen = 0;
        if (!IsValidCellOffset(offset, _data.Length))
            return false;
        int size = (int)ReadI32(_data, offset);
        if (size <= 0 || size < 4)
            return false;
        cellSize = size;
        dataStart = offset + 4;
        dataLen = size - 4;
        return dataStart + dataLen <= _data.Length;
    }

    private static bool IsValidCellOffset(int offset, int length)
        => offset >= BaseBlockSize && offset + 4 <= length && (offset & 0x7) == 0;

    private bool IsAscii(int offset, string text, int count)
    {
        if (offset + count > _data.Length)
            return false;
        for (int i = 0; i < count; i++)
        {
            if (_data[offset + i] != (byte)text[i])
                return false;
        }
        return true;
    }

    private string Ascii(int offset, int count)
    {
        if (offset < 0 || offset + count > _data.Length)
            return "";
        return Encoding.ASCII.GetString(_data, offset, count);
    }

    private string DecodeUtf16(int offset, int byteLen)
    {
        int end = offset + (byteLen & ~1);
        if (end > _data.Length)
            end = _data.Length & ~1;
        for (int i = offset; i + 1 < end; i += 2)
        {
            if (_data[i] == 0 && _data[i + 1] == 0)
            {
                end = i;
                break;
            }
        }
        if (end <= offset)
            return "";
        try
        {
            return Encoding.Unicode.GetString(_data, offset, end - offset);
        }
        catch
        {
            return "";
        }
    }

    private int ReadU16At(int offset) => offset >= 0 && offset + 2 <= _data.Length ? BitConverter.ToUInt16(_data, offset) : 0;
    private int ReadI32At(int offset) => offset >= 0 && offset + 4 <= _data.Length ? BitConverter.ToInt32(_data, offset) : 0;
    private uint ReadU32At(int offset) => offset >= 0 && offset + 4 <= _data.Length ? BitConverter.ToUInt32(_data, offset) : 0;
    private long ReadI64At(int offset) => offset >= 0 && offset + 8 <= _data.Length ? BitConverter.ToInt64(_data, offset) : 0;
    private static uint ReadU32(byte[] data, int offset) => offset >= 0 && offset + 4 <= data.Length ? BitConverter.ToUInt32(data, offset) : 0;
    private static int ReadI32(byte[] data, int offset) => offset >= 0 && offset + 4 <= data.Length ? BitConverter.ToInt32(data, offset) : 0;
}
