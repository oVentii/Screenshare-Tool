using System.Runtime.InteropServices;

internal sealed class PrefetchParser
{
    private const int MinSize = 0x100;
    private const int MaxFileBytes = 64 * 1024 * 1024;

    private const int OffFileNameStrings = 0x64;
    private const int OffFileNameStringsSize = 0x68;
    private const int OffExecTimes = 0x80;
    private const int ExecSlotCount = 8;

    private const uint MamMagic = 0x004D414D;

    private byte[] _data = Array.Empty<byte>();

    public bool Success => _data.Length != 0;

    public PrefetchParser(string filePath)
    {
        byte[]? content = ForensicUtil.ReadAllBytesBounded(filePath, MaxFileBytes);
        if (content is null || content.Length < MinSize)
            return;

        if (content[0] == (byte)'M' && content[1] == (byte)'A' && content[2] == (byte)'M')
        {
            byte[]? decompressed = DecompressMam(content);
            if (decompressed is null || decompressed.Length < MinSize)
                return;
            if (decompressed[4] != (byte)'S' || decompressed[5] != (byte)'C' ||
                decompressed[6] != (byte)'C' || decompressed[7] != (byte)'A')
                return;
            _data = decompressed;
        }
        else if (content[4] == (byte)'S' && content[5] == (byte)'C' &&
                 content[6] == (byte)'C' && content[7] == (byte)'A')
        {
            _data = content;
        }
    }

    private static byte[]? DecompressMam(byte[] content)
    {
        if (content.Length < 8)
            return null;

        uint signature = BitConverter.ToUInt32(content, 0);
        uint decompressedSize = BitConverter.ToUInt32(content, 4);
        if ((signature & 0x00FFFFFF) != MamMagic)
            return null;
        if (((signature & 0xF0000000) >> 28) != 0)
            return null;
        if (decompressedSize == 0 || decompressedSize > (uint)MaxFileBytes)
            return null;

        ushort format = (ushort)((signature & 0x0F000000) >> 24);
        if (PrefetchNative.GetCompressionWorkSpaceSize(format, out uint wsSize, out _) != 0)
            return null;

        byte[] compressed = new byte[content.Length - 8];
        Buffer.BlockCopy(content, 8, compressed, 0, compressed.Length);
        byte[] decompressed = new byte[decompressedSize];

        IntPtr workspace = Marshal.AllocHGlobal((int)Math.Max(wsSize, 1));
        try
        {
            int status = PrefetchNative.DecompressBufferEx(
                format, decompressed, decompressedSize,
                compressed, (uint)compressed.Length,
                out _, workspace);
            if (status != 0)
                return null;
        }
        finally
        {
            Marshal.FreeHGlobal(workspace);
        }

        return decompressed;
    }

    private bool TryReadI32(int offset, out int value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > _data.Length)
            return false;
        value = BitConverter.ToInt32(_data, offset);
        return true;
    }

    private bool TryReadI64(int offset, out long value)
    {
        value = 0;
        if (offset < 0 || offset + 8 > _data.Length)
            return false;
        value = BitConverter.ToInt64(_data, offset);
        return true;
    }

    private int FileNameStringsOffset() => TryReadI32(OffFileNameStrings, out int v) ? v : 0;
    private int FileNameStringsSize() => TryReadI32(OffFileNameStringsSize, out int v) ? v : 0;

    private long ExecutedTimestamp()
    {
        if (OffExecTimes + 8 > _data.Length)
            return 0;
        return BitConverter.ToInt64(_data, OffExecTimes);
    }

    public List<string> GetFilenamesStrings()
    {
        var result = new List<string>();
        int offset = FileNameStringsOffset();
        int size = FileNameStringsSize();
        if (offset < 0 || size <= 0 || offset >= _data.Length)
            return result;
        size = Math.Min(size, _data.Length - offset);

        int end = offset + (size & ~1);
        var name = new System.Text.StringBuilder();
        for (int i = offset; i < end; i += 2)
        {
            char ch = (char)(_data[i] | (_data[i + 1] << 8));
            if (ch == '\0')
            {
                if (name.Length > 0)
                {
                    result.Add(name.ToString());
                    name.Clear();
                }
                continue;
            }
            name.Append(ch);
        }

        return result;
    }

    public long[] LastEightExecutionTimes()
    {
        var times = new long[ExecSlotCount];
        if (_data.Length <= OffExecTimes)
            return times;

        int available = _data.Length - OffExecTimes;
        int count = Math.Min(ExecSlotCount, available / 8);
        for (int i = 0; i < count; i++)
        {
            long ft = BitConverter.ToInt64(_data, OffExecTimes + i * 8);
            if (ft == 0)
                continue;
            try
            {
                // True UTC unix time: no local conversion here, the display
                // layer (FormatUnix) localizes. Converting here would shift
                // every timestamp by the machine timezone offset.
                times[i] = DateTime.FromFileTimeUtc(ft).Ticks / TimeSpan.TicksPerSecond
                    - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond;
            }
            catch
            {
            }
        }

        return times;
    }

    public long ExecutedTime()
    {
        long ft = ExecutedTimestamp();
        if (ft == 0)
            return 0;
        try
        {
            return DateTime.FromFileTimeUtc(ft).Ticks / TimeSpan.TicksPerSecond
                - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond;
        }
        catch
        {
            return 0;
        }
    }

    public List<string> LastRunTimesExact()
    {
        var list = new List<string>(ExecSlotCount);
        for (int i = 0; i < ExecSlotCount; i++)
        {
            string s = "";
            if (TryReadI64(OffExecTimes + i * 8, out long ft) && ft != 0)
            {
                try
                {
                    s = DateTime.FromFileTimeUtc(ft).ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm:ss.fff");
                }
                catch
                {
                }
            }
            list.Add(s);
        }
        return list;
    }
}
