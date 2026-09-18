using System.Buffers.Binary;
using System.Text;

internal static class LevelDbReader
{
    internal const byte KFullType = 1, KFirstType = 2, KMiddleType = 3, KLastType = 4;

    private static readonly byte[] Magic = { 0x57, 0xfb, 0x80, 0x8b, 0x24, 0x75, 0x47, 0xdb };

    internal static List<(string Key, string Value)> ReadLog(byte[] data)
    {
        var result = new List<(string, string)>();
        if (data == null || data.Length < 7) return result;
        const int BlockSize = 32768;
        try
        {
            var fragment = new List<byte>(4096);
            int pos = 0;
            while (pos + 7 <= data.Length)
            {
                int length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 4, 2));
                byte type = data[pos + 6];
                if (length == 0 && type == 0)
                {
                    pos = (int)Math.Min(data.Length, ((pos / (long)BlockSize) + 1) * BlockSize);
                    continue;
                }
                pos += 7;
                if (length == 0 || pos + length > data.Length) break;

                switch (type)
                {
                    case KFullType:
                        ParseWriteBatch(data.AsSpan(pos, length), result);
                        break;
                    case KFirstType:
                        fragment.Clear();
                        fragment.AddRange(data.AsSpan(pos, length).ToArray());
                        break;
                    case KMiddleType:
                        fragment.AddRange(data.AsSpan(pos, length).ToArray());
                        break;
                    case KLastType:
                        fragment.AddRange(data.AsSpan(pos, length).ToArray());
                        ParseWriteBatch(fragment.ToArray(), result);
                        fragment.Clear();
                        break;
                }
                pos += length;
            }
        }
        catch { }
        return result;
    }

    internal static List<(string Key, string Value)> ReadTable(byte[] data)
    {
        var result = new List<(string, string)>();
        if (data == null || data.Length < 48) return result;
        try
        {
            var footer = data.AsSpan(data.Length - 48, 48);
            if (!footer.Slice(40, 8).SequenceEqual(Magic)) return result;

            int fp = 0;
            _ = ReadVarint(footer, ref fp);
            _ = ReadVarint(footer, ref fp);
            ulong indexOffset = ReadVarint(footer, ref fp);
            ulong indexSize = ReadVarint(footer, ref fp);
            if (indexOffset >= (ulong)data.Length || indexSize < 6) return result;
            if (indexOffset + indexSize > (ulong)data.Length) return result;

            var indexBlock = ReadBlock(data, (int)indexOffset, (int)indexSize);
            if (indexBlock == null) return result;

            var handles = new List<(ulong Offset, ulong Size)>();
            ParseEntries(indexBlock, (key, value) =>
            {
                int vp = 0;
                ulong offset = ReadVarint(value, ref vp);
                ulong size = ReadVarint(value, ref vp);
                handles.Add((offset, size));
            });

            foreach (var h in handles)
            {
                if (h.Offset >= (ulong)data.Length || h.Size < 6) continue;
                if (h.Offset + h.Size > (ulong)data.Length) continue;
                var block = ReadBlock(data, (int)h.Offset, (int)h.Size);
                if (block == null) continue;
                ParseEntries(block, (key, value) => result.Add((Decode(key), Decode(value))));
            }
        }
        catch { }
        return result;
    }

    private static void ParseWriteBatch(ReadOnlySpan<byte> payload, List<(string, string)> result)
    {
        if (payload.Length <= 8) return;
        var batch = payload.Slice(8);
        int p = 0;
        _ = ReadVarint(batch, ref p);
        ParseEntries(batch.Slice(p), (key, value) => result.Add((Decode(key), Decode(value))));
    }

    private static void ParseEntries(ReadOnlySpan<byte> block, Action<byte[], byte[]> onEntry)
    {
        int p = 0;
        byte[]? prefix = null;
        while (p < block.Length)
        {
            int shared = (int)ReadVarint(block, ref p);
            int nonShared = (int)ReadVarint(block, ref p);
            int valueLen = (int)ReadVarint(block, ref p);
            int keyLen = shared + nonShared;
            if (keyLen < 0 || valueLen < 0) return;
            if (keyLen > block.Length - p || valueLen > block.Length - p - keyLen) return;

            var key = new byte[keyLen];
            if (shared > 0)
            {
                if (prefix is null || shared > prefix.Length) return;
                Array.Copy(prefix, 0, key, 0, shared);
            }
            block.Slice(p, nonShared).CopyTo(key.AsSpan(shared));
            p += nonShared;

            var value = block.Slice(p, valueLen).ToArray();
            p += valueLen;

            if (keyLen > 0) prefix = key;
            onEntry(key, value);
        }
    }

    private static byte[]? ReadBlock(byte[] file, int offset, int size)
    {
        if (offset < 0 || size < 1 || offset > file.Length - size - 5) return null;
        byte type = file[offset + size];
        var block = file.AsSpan(offset, size);
        if (type == 1) return SnappyDecode(block);
        return block.ToArray();
    }

    internal static byte[]? SnappyDecode(ReadOnlySpan<byte> src)
    {
        int pos = 0;
        ulong uncompressed = ReadVarint(src, ref pos);
        if (uncompressed == 0 || uncompressed > int.MaxValue) return null;
        int target = (int)uncompressed;
        var output = new byte[target];
        int outPos = 0;

        while (pos < src.Length && outPos < target)
        {
            byte tag = src[pos++];
            int type = tag & 3;
            if (type == 0)
            {
                int len = (tag >> 2) + 1;
                if ((tag >> 2) >= 60)
                {
                    int extra = (tag >> 2) - 59;
                    if (pos + extra > src.Length) return null;
                    len = 0;
                    for (int i = 0; i < extra; i++)
                        len |= src[pos + i] << (8 * i);
                    len++;
                    pos += extra;
                }
                if (len < 0 || pos + len > src.Length || outPos + len > target) return null;
                src.Slice(pos, len).CopyTo(output.AsSpan(outPos));
                pos += len;
                outPos += len;
            }
            else
            {
                int len, offset;
                switch (type)
                {
                    case 1:
                        if (pos >= src.Length) return null;
                        len = ((tag >> 2) & 0x7) + 4;
                        offset = ((tag >> 5) << 8) | src[pos++];
                        break;
                    case 2:
                        if (pos + 2 > src.Length) return null;
                        len = (tag >> 2) + 1;
                        offset = src[pos] | (src[pos + 1] << 8);
                        pos += 2;
                        break;
                    default:
                        if (pos + 4 > src.Length) return null;
                        len = (tag >> 2) + 1;
                        offset = src[pos] | (src[pos + 1] << 8) | (src[pos + 2] << 16) | (src[pos + 3] << 24);
                        pos += 4;
                        break;
                }
                if (offset <= 0 || offset > outPos || outPos + len > target) return null;
                for (int i = 0; i < len; i++)
                {
                    output[outPos] = output[outPos - offset];
                    outPos++;
                }
            }
        }
        return outPos == target ? output : null;
    }

    private static string Decode(byte[] bytes)
    {
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return string.Empty; }
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int pos)
    {
        ulong result = 0;
        int shift = 0;
        while (pos < data.Length && shift < 64)
        {
            byte b = data[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
        return result;
    }
}
