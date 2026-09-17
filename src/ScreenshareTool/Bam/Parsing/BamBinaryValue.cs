using System.Text;



public sealed class BamField
{
    public required string Name { get; init; }
    public required int Offset { get; init; }
    public required int Size { get; init; }
    public required string RawHex { get; init; }
    public required string Decoded { get; init; }
    public required string Validation { get; init; }
}


public sealed class BamBinaryValue
{
    public required int DataLength { get; set; }
    public required BamValueState State { get; set; }
    
    public ulong RawFileTime { get; set; }
    
    public long UnixSeconds { get; set; }
    
    public string? UtcString { get; set; }
    public TimestampValidity TimestampValidity { get; set; } = TimestampValidity.Unknown;
    
    public ulong ReservedRegion { get; set; }
    public bool ReservedRegionNonZero { get; set; }
    
    public uint Flags { get; set; }
    public bool IsWindowsApp { get; set; }
    
    public uint UnknownField { get; set; }
    
    public string TrailingHex { get; set; } = "";
    
    public List<BamField> Fields { get; } = new();
}


internal static class BamBinaryValueParser
{
    
    public const int ExpectedLength = 24;
    
    private const int MinLength = 8;
    
    private static readonly ulong FileTime1980 = (ulong)new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    
    private static readonly ulong FileTime2400 = (ulong)new DateTime(2400, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    
    private static readonly ulong FileTime2100 = (ulong)new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
    
    private const uint WindowsAppFlag = 0x00000001;

    public static BamBinaryValue Parse(byte[] data)
    {
        var value = new BamBinaryValue
        {
            DataLength = data.Length,
            State = data.Length switch
            {
                ExpectedLength => BamValueState.ValidKnownLength,
                > ExpectedLength => BamValueState.LongerThanExpected,
                >= MinLength => BamValueState.ShorterThanExpected,
                _ => BamValueState.UnexpectedLength,
            },
        };

        if (data.Length < MinLength)
        {
            value.Fields.Add(Field("Data", 0, data.Length, Hex(data, 0, data.Length), "—", value.State.ToString()));
            return value;
        }

        value.RawFileTime = BitConverter.ToUInt64(data, 0);
        (value.UnixSeconds, value.UtcString, value.TimestampValidity) = ValidateFileTime(value.RawFileTime);
        value.Fields.Add(Field("ExecutionTime", 0, 8, Hex(data, 0, 8),
            value.UtcString ?? $"0x{value.RawFileTime:X16}", value.TimestampValidity.ToString()));

        if (data.Length >= 16)
        {
            value.ReservedRegion = BitConverter.ToUInt64(data, 8);
            value.ReservedRegionNonZero = value.ReservedRegion != 0;
            value.Fields.Add(Field("Reserved", 8, 8, Hex(data, 8, 8),
                value.ReservedRegion == 0 ? "all zero" : $"0x{value.ReservedRegion:X16}",
                value.ReservedRegionNonZero ? "NonZeroAnomaly" : "Valid"));
        }

        if (data.Length >= 20)
        {
            value.Flags = BitConverter.ToUInt32(data, 16);
            value.IsWindowsApp = (value.Flags & WindowsAppFlag) != 0;
            value.Fields.Add(Field("Flags", 16, 4, Hex(data, 16, 4),
                $"0x{value.Flags:X8}{(value.IsWindowsApp ? " (windows app bit)" : "")}",
                "Unknown"));
        }

        if (data.Length >= 24)
        {
            value.UnknownField = BitConverter.ToUInt32(data, 20);
            value.Fields.Add(Field("Unknown", 20, 4, Hex(data, 20, 4),
                $"0x{value.UnknownField:X8}", "Unknown"));
        }

        if (data.Length > ExpectedLength)
        {
            value.TrailingHex = Hex(data, ExpectedLength, data.Length - ExpectedLength);
            value.Fields.Add(Field("TrailingData", ExpectedLength, data.Length - ExpectedLength,
                value.TrailingHex, $"{data.Length - ExpectedLength} extra bytes", "Preserved"));
        }

        return value;
    }

    private static (long Unix, string? Utc, TimestampValidity Validity) ValidateFileTime(ulong raw)
    {
        if (raw == 0)
            return (0, null, TimestampValidity.Unknown); 
        if (raw < FileTime1980)
            return (0, null, TimestampValidity.Invalid); 
        if (raw > FileTime2400)
            return (0, null, TimestampValidity.Invalid); 

        long unix = ForensicUtil.FileTimeToUnixTime(raw);
        if (raw > FileTime2100)
        {
            
            
            string utc = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            return (unix, utc + "Z", TimestampValidity.Suspicious);
        }
        if (!ForensicUtil.IsPlausibleUnixTime(unix))
            return (0, null, TimestampValidity.Invalid);

        string utcExact = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        return (unix, utcExact + "Z", TimestampValidity.Exact);
    }

    private static BamField Field(string name, int offset, int size, string raw, string decoded, string validation)
        => new() { Name = name, Offset = offset, Size = size, RawHex = raw, Decoded = decoded, Validation = validation };

    private static string Hex(byte[] data, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset >= data.Length)
            return "";
        int n = Math.Min(length, data.Length - offset);
        return Convert.ToHexString(data, offset, n);
    }
}
