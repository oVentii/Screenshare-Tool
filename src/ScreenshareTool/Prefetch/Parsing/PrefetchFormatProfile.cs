

public enum PrefetchVariant
{
    
    Default,
    
    Header128,
    
    Header130,
}







internal sealed class PrefetchFormatProfile
{
    
    internal const int OffVersion = 0x00;
    internal const int OffSccaMagic = 0x04;
    internal const int OffDeclaredSize = 0x0C;
    internal const int OffExeName = 0x10;
    internal const int ExeNameFieldBytes = 60;      
    internal const int OffPrefetchHash = 0x4C;
    internal const int OffBootFlag = 0x50;
    internal const int HeaderRegionEnd = 0x54;

    
    internal const int OffMetricsOffset = 0x54;
    internal const int OffMetricsCount = 0x58;
    internal const int OffTraceChainsOffset = 0x5C;
    internal const int OffTraceChainsCount = 0x60;
    internal const int OffStringsOffset = 0x64;
    internal const int OffStringsSize = 0x68;
    internal const int OffVolumesOffset = 0x6C;
    internal const int OffVolumesCount = 0x70;

    public required int Version { get; init; }
    public required string WindowsFamily { get; init; }
    public required PrefetchVariant Variant { get; init; }
    public required SupportLevel Support { get; init; }

    
    public required int RunCountOffset { get; init; }
    
    public int RunCountFallbackOffset { get; init; }

    
    public required int ExecTimeOffset { get; init; }
    public required int ExecTimeCount { get; init; }

    
    public required int MetricsEntrySize { get; init; }
    public required int MetricsChainIndexOffset { get; init; }
    public required int MetricsFlagsOffset { get; init; }
    public required int MetricsFileRefOffset { get; init; }

    
    public required int TraceChainEntrySize { get; init; }

    
    public required int VolumeEntrySize { get; init; }
    public required int VolumeDevicePathOffset { get; init; }
    public required int VolumeDevicePathCharsOffset { get; init; }
    public required int VolumeSerialOffset { get; init; }
    public required int VolumeDirsOffset { get; init; }
    public required int VolumeDirsCountOffset { get; init; }

    
    
    public int HashStringFieldOffset { get; init; }
    public bool HasHashString { get; init; }

    
    public bool HasMetricsFlags { get; init; }
    public bool HasBootFlag { get; init; }
    public bool HasTraceChains { get; init; }
    public int MaxReferencedFiles { get; init; } = 4096;

    public string VersionName => Version switch
    {
        17 => "Windows XP / 2003",
        23 => "Windows Vista / 7",
        26 => "Windows 8 / 8.1",
        30 => "Windows 10",
        31 => "Windows 11",
        _ => $"Unknown ({Version})"
    };

    public override string ToString()
        => $"v{Version} ({WindowsFamily}, {Variant})";
}



internal static class PrefetchFormatRegistry
{
    public const int Version17 = 17;
    public const int Version23 = 23;
    public const int Version26 = 26;
    public const int Version30 = 30;
    public const int Version31 = 31;

    
    private const int MetricsOffsetHeader128 = 0x128;
    private const int MetricsOffsetHeader130 = 0x130;

    private static readonly PrefetchFormatProfile V17 = new()
    {
        Version = 17,
        WindowsFamily = "Windows XP / 2003",
        Variant = PrefetchVariant.Default,
        Support = SupportLevel.Full,
        RunCountOffset = 0x90,
        ExecTimeOffset = 0x78,
        ExecTimeCount = 1,
        MetricsEntrySize = 20,          
        MetricsChainIndexOffset = -1,
        MetricsFlagsOffset = -1,
        MetricsFileRefOffset = -1,
        TraceChainEntrySize = 12,
        VolumeEntrySize = 40,
        VolumeDevicePathOffset = 0,
        VolumeDevicePathCharsOffset = 4,
        VolumeSerialOffset = 16,
        VolumeDirsOffset = 28,
        VolumeDirsCountOffset = 32,
        HasMetricsFlags = false,
        HasBootFlag = false,
        HasTraceChains = false,
    };

    private static readonly PrefetchFormatProfile V23 = new()
    {
        Version = 23,
        WindowsFamily = "Windows Vista / 7",
        Variant = PrefetchVariant.Default,
        Support = SupportLevel.Full,
        RunCountOffset = 0x98,
        ExecTimeOffset = 0x80,
        ExecTimeCount = 1,
        MetricsEntrySize = 32,
        MetricsChainIndexOffset = 0,
        MetricsFlagsOffset = 20,
        MetricsFileRefOffset = 24,
        TraceChainEntrySize = 12,
        VolumeEntrySize = 104,
        VolumeDevicePathOffset = 0,
        VolumeDevicePathCharsOffset = 4,
        VolumeSerialOffset = 16,
        VolumeDirsOffset = 28,
        VolumeDirsCountOffset = 32,
        HasMetricsFlags = true,
        HasBootFlag = true,
        HasTraceChains = true,
    };

    private static readonly PrefetchFormatProfile V26 = new()
    {
        Version = 26,
        WindowsFamily = "Windows 8 / 8.1",
        Variant = PrefetchVariant.Default,
        Support = SupportLevel.Full,
        RunCountOffset = 0xD0,
        ExecTimeOffset = 0x80,
        ExecTimeCount = 8,
        MetricsEntrySize = 32,
        MetricsChainIndexOffset = 0,
        MetricsFlagsOffset = 20,
        MetricsFileRefOffset = 24,
        TraceChainEntrySize = 12,
        VolumeEntrySize = 104,
        VolumeDevicePathOffset = 0,
        VolumeDevicePathCharsOffset = 4,
        VolumeSerialOffset = 16,
        VolumeDirsOffset = 28,
        VolumeDirsCountOffset = 32,
        HasMetricsFlags = true,
        HasBootFlag = true,
        HasTraceChains = true,
    };

    private static readonly PrefetchFormatProfile V30_128 = new()
    {
        Version = 30,
        WindowsFamily = "Windows 10",
        Variant = PrefetchVariant.Header128,
        Support = SupportLevel.Full,
        RunCountOffset = 0xC8,
        RunCountFallbackOffset = 0xD0,
        ExecTimeOffset = 0x80,
        ExecTimeCount = 8,
        MetricsEntrySize = 32,
        MetricsChainIndexOffset = 0,
        MetricsFlagsOffset = 20,
        MetricsFileRefOffset = 24,
        TraceChainEntrySize = 8,
        VolumeEntrySize = 96,
        VolumeDevicePathOffset = 0,
        VolumeDevicePathCharsOffset = 4,
        VolumeSerialOffset = 16,
        VolumeDirsOffset = 28,
        VolumeDirsCountOffset = 32,
        HashStringFieldOffset = 0xD4,
        HasHashString = true,
        HasMetricsFlags = true,
        HasBootFlag = true,
        HasTraceChains = true,
    };

    private static readonly PrefetchFormatProfile V30_130 = new()
    {
        Version = 30,
        WindowsFamily = "Windows 10",
        Variant = PrefetchVariant.Header130,
        Support = SupportLevel.Full,
        RunCountOffset = 0xD0,
        ExecTimeOffset = 0x80,
        ExecTimeCount = 8,
        MetricsEntrySize = 32,
        MetricsChainIndexOffset = 0,
        MetricsFlagsOffset = 20,
        MetricsFileRefOffset = 24,
        TraceChainEntrySize = 8,
        VolumeEntrySize = 96,
        VolumeDevicePathOffset = 0,
        VolumeDevicePathCharsOffset = 4,
        VolumeSerialOffset = 16,
        VolumeDirsOffset = 28,
        VolumeDirsCountOffset = 32,
        HashStringFieldOffset = 0xDC,
        HasHashString = true,
        HasMetricsFlags = true,
        HasBootFlag = true,
        HasTraceChains = true,
    };

    private static readonly PrefetchFormatProfile V31_128 = new()
    {
        Version = 31,
        WindowsFamily = "Windows 11",
        Variant = PrefetchVariant.Header128,
        Support = SupportLevel.Full,
        RunCountOffset = 0xC8,
        RunCountFallbackOffset = 0xD0,
        ExecTimeOffset = 0x80,
        ExecTimeCount = 8,
        MetricsEntrySize = 32,
        MetricsChainIndexOffset = 0,
        MetricsFlagsOffset = 20,
        MetricsFileRefOffset = 24,
        TraceChainEntrySize = 8,
        VolumeEntrySize = 96,
        VolumeDevicePathOffset = 0,
        VolumeDevicePathCharsOffset = 4,
        VolumeSerialOffset = 16,
        VolumeDirsOffset = 28,
        VolumeDirsCountOffset = 32,
        HashStringFieldOffset = 0xD4,
        HasHashString = true,
        HasMetricsFlags = true,
        HasBootFlag = true,
        HasTraceChains = true,
    };

    private static readonly PrefetchFormatProfile V31_130 = new()
    {
        Version = 31,
        WindowsFamily = "Windows 11",
        Variant = PrefetchVariant.Header130,
        Support = SupportLevel.Full,
        RunCountOffset = 0xD0,
        ExecTimeOffset = 0x80,
        ExecTimeCount = 8,
        MetricsEntrySize = 32,
        MetricsChainIndexOffset = 0,
        MetricsFlagsOffset = 20,
        MetricsFileRefOffset = 24,
        TraceChainEntrySize = 8,
        VolumeEntrySize = 96,
        VolumeDevicePathOffset = 0,
        VolumeDevicePathCharsOffset = 4,
        VolumeSerialOffset = 16,
        VolumeDirsOffset = 28,
        VolumeDirsCountOffset = 32,
        HashStringFieldOffset = 0xDC,
        HasHashString = true,
        HasMetricsFlags = true,
        HasBootFlag = true,
        HasTraceChains = true,
    };

    public static bool IsKnownVersion(int version)
        => version is Version17 or Version23 or Version26 or Version30 or Version31;

    
    
    
    
    public static bool TryResolve(int version, int metricsOffsetFieldValue, out PrefetchFormatProfile profile)
    {
        if (version is Version30 or Version31)
        {
            bool header130 = metricsOffsetFieldValue == MetricsOffsetHeader130;
            profile = (version, header130) switch
            {
                (Version30, true) => V30_130,
                (Version30, false) => V30_128,
                (Version31, true) => V31_130,
                _ => V31_128,
            };
            return true;
        }

        profile = version switch
        {
            Version17 => V17,
            Version23 => V23,
            Version26 => V26,
            _ => null!,
        };
        return profile is not null;
    }
}
