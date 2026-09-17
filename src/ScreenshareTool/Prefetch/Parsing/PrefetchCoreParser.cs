using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;




public sealed class PrefetchParseResult
{
    public PrefetchArtifact? Artifact { get; init; }
    public PrefetchParseState State { get; init; }
    public List<PrefetchDiagnostic> Errors { get; } = new();
    public List<PrefetchDiagnostic> Warnings { get; } = new();
}














internal static class PrefetchCoreParser
{
    internal const int MinPfSize = 0x100;
    internal const int MaxCompressedPfBytes = 8 * 1024 * 1024;
    private const int MaxDecompressedPfBytes = 16 * 1024 * 1024;
    private const int MaxWorkspaceBytes = 64 * 1024 * 1024;
    private const int MaxUnknownRegions = 32;
    private const int UnknownRegionPreviewBytes = 16;

    private const uint MamSig = 0x004D414D;

    
    public static PrefetchKind Identify(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 8)
            return PrefetchKind.NotPrefetch;
        ReadOnlySpan<byte> span = data.Span;
        if (span[4] == (byte)'S' && span[5] == (byte)'C' && span[6] == (byte)'C' && span[7] == (byte)'A')
            return PrefetchKind.Scca;
        uint sig = BitConverter.ToUInt32(span);
        if ((sig & 0x00FFFFFFu) == MamSig)
            return PrefetchKind.Mam;
        return PrefetchKind.NotPrefetch;
    }

    public static PrefetchParseResult Parse(ReadOnlyMemory<byte> input, string? sourcePath = null)
    {
        var errors = new List<PrefetchDiagnostic>();
        var warnings = new List<PrefetchDiagnostic>();
        var notes = new List<PrefetchDiagnostic>();

        if (input.Length < MinPfSize)
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Header, $"Input too small to be a prefetch ({input.Length} bytes)"));
            return Finalize(null, PrefetchParseState.Invalid, errors, warnings);
        }

        byte[] raw = input.ToArray();
        PrefetchKind kind = Identify(raw);

        int compressedPayloadSize = 0;
        string? compression = null;
        byte[] data;
        if (kind == PrefetchKind.Mam)
        {
            byte[]? decompressed = DecompressMam(raw, errors);
            if (decompressed is null)
            {
                warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Compression,
                    "MAM container present but no compression format produced a valid SCCA payload"));
                return Finalize(null, PrefetchParseState.Corrupted, errors, warnings);
            }
            data = decompressed;
            compressedPayloadSize = raw.Length - 8;
            compression = MamFormatName(raw);
        }
        else if (kind == PrefetchKind.Scca)
        {
            data = raw;
        }
        else
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Header, "No SCCA or MAM signature present"));
            return Finalize(null, PrefetchParseState.Invalid, errors, warnings);
        }

        if (data.Length < MinPfSize)
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Header, $"Decompressed payload too small ({data.Length} bytes)"));
            return Finalize(null, PrefetchParseState.Corrupted, errors, warnings);
        }

        
        int version = ReadI32(data, PrefetchFormatProfile.OffVersion);
        if (!PrefetchFormatRegistry.IsKnownVersion(version))
            return UnknownVersion(data, version, sourcePath, errors, warnings, notes);

        int metricsOffsetField = ReadI32(data, PrefetchFormatProfile.OffMetricsOffset);
        if (!PrefetchFormatRegistry.TryResolve(version, metricsOffsetField, out PrefetchFormatProfile profile))
            return UnknownVersion(data, version, sourcePath, errors, warnings, notes);

        if (version is 30 or 31 && metricsOffsetField is not (0x128 or 0x130))
        {
            warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Version,
                $"v{version} metrics offset 0x{metricsOffsetField:X} is neither 0x128 nor 0x130 — header variant assumed from fallback semantics"));
        }

        
        int sccaMagic = ReadI32(data, PrefetchFormatProfile.OffSccaMagic);
        int declaredSize = ReadI32(data, PrefetchFormatProfile.OffDeclaredSize);
        string headerExe = ExtractHeaderExecutableName(data);
        uint storedHash = (uint)ReadI32(data, PrefetchFormatProfile.OffPrefetchHash);
        string? hashString = ExtractHashString(data, profile);

        SizeValidation sizeState = ClassifySize(declaredSize, data.Length, warnings);
        bool bootByFlag = profile.HasBootFlag
                          && (ReadI32(data, PrefetchFormatProfile.OffBootFlag) & 0x01) != 0;
        string fileName = string.IsNullOrEmpty(sourcePath) ? "" : Path.GetFileName(sourcePath);
        var filename = PrefetchFilenameParser.Parse(fileName);
        bool isBoot = filename.IsBootPrefetch || bootByFlag;

        
        int metricsOffset = ReadI32(data, PrefetchFormatProfile.OffMetricsOffset);
        int metricsCount = ReadI32(data, PrefetchFormatProfile.OffMetricsCount);
        int chainsOffset = ReadI32(data, PrefetchFormatProfile.OffTraceChainsOffset);
        int chainsCount = ReadI32(data, PrefetchFormatProfile.OffTraceChainsCount);
        int stringsOffset = ReadI32(data, PrefetchFormatProfile.OffStringsOffset);
        int stringsSize = ReadI32(data, PrefetchFormatProfile.OffStringsSize);
        int volumesOffset = ReadI32(data, PrefetchFormatProfile.OffVolumesOffset);
        int volumesCount = ReadI32(data, PrefetchFormatProfile.OffVolumesCount);

        var fileInfo = new PrefetchFileInfo
        {
            MetricsOffset = metricsOffset,
            MetricsCount = metricsCount,
            TraceChainsOffset = chainsOffset,
            TraceChainsCount = chainsCount,
            StringsOffset = stringsOffset,
            StringsSize = stringsSize,
            VolumesOffset = volumesOffset,
            VolumesCount = volumesCount,
        };

        
        int runCount = ReadRunCount(data, profile, warnings);

        var execTimes = ReadExecutionTimes(data, profile, warnings);
        var strings = ReadFileStrings(data, stringsOffset, stringsSize, profile, errors, warnings);
        var metrics = ReadMetrics(data, profile, metricsOffset, metricsCount, errors, warnings);
        var chains = ReadTraceChains(data, profile, chainsOffset, chainsCount, metrics, errors, warnings);
        var (volumes, directoryStrings) = ReadVolumes(data, profile, volumesOffset, volumesCount, errors, warnings);

        bool metricsCountMatchesStrings = metricsCount > 0 && strings.Count > 0 && metricsCount == strings.Count;
        bool integrityMismatch = (metricsCount > 0 && strings.Count > 0 && metricsCount != strings.Count)
                                 || (metrics.Count > 0 && metrics.Count != strings.Count);
        fileInfo = new PrefetchFileInfo
        {
            MetricsOffset = metricsOffset,
            MetricsCount = metricsCount,
            TraceChainsOffset = chainsOffset,
            TraceChainsCount = chainsCount,
            StringsOffset = stringsOffset,
            StringsSize = stringsSize,
            VolumesOffset = volumesOffset,
            VolumesCount = volumesCount,
            MetricsCountMatchesStrings = metricsCountMatchesStrings,
        };
        if (integrityMismatch)
            warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Integrity,
                $"Metrics count ({metricsCount}) disagrees with parsed file strings ({strings.Count})"));

        
        var identity = BuildIdentity(filename, headerExe, storedHash, hashString, version, isBoot);

        
        var regions = BuildRegions(data.Length, declaredSize, sizeState,
            metricsOffset, metricsCount, profile,
            chainsOffset, chainsCount, profile,
            stringsOffset, stringsSize,
            volumesOffset, volumesCount, profile);
        var overlaps = DetectOverlaps(regions, profile);
        var unknownRegions = FindUnknownRegions(data, regions, sizeState, warnings);

        
        PrefetchParseState state = PrefetchParseState.Success;
        foreach (var e in errors)
        {
            if (e.Code is PrefetchErrorCode.Bounds or PrefetchErrorCode.Metrics or PrefetchErrorCode.TraceChain
                or PrefetchErrorCode.Volume or PrefetchErrorCode.Compression)
            {
                state = PrefetchParseState.Partial;
                break;
            }
        }
        if (sizeState == SizeValidation.Truncated)
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Integrity,
                $"Header declares {declaredSize} bytes but only {data.Length} are present — truncated"));
            state = PrefetchParseState.Partial;
        }

        double confidence = PrefetchConfidence.Adjust(
            PrefetchConfidence.FromState(state),
            unknownRegions.Count,
            sizeState is SizeValidation.SizeMismatch or SizeValidation.Truncated or SizeValidation.TrailingData,
            integrityMismatch);

        var artifact = new PrefetchArtifact
        {
            Kind = kind,
            State = state,
            Confidence = confidence,
            FormatVersion = version,
            VersionName = profile.VersionName,
            Variant = profile.Variant,
            Support = profile.Support,
            SccaMagic = sccaMagic,
            DeclaredFileSize = declaredSize,
            SizeState = sizeState,
            HeaderExecutableName = headerExe,
            StoredPrefetchHash = storedHash,
            HashString = hashString,
            IsBootPrefetch = isBoot,
            FileInfo = fileInfo,
            RunCount = runCount,
            RunCountPlausible = runCount > 0,
            ExecutionTimes = execTimes,
            Metrics = metrics,
            TraceChains = chains,
            FileStrings = strings,
            Volumes = volumes,
            DirectoryStrings = directoryStrings,
            Identity = identity,
            Regions = regions,
            Overlaps = overlaps,
            UnknownRegions = unknownRegions,
            Compression = compression,
            CompressedPayloadSize = compressedPayloadSize,
            Validation = new PrefetchValidation
            {
                State = state,
                Errors = errors,
                Warnings = warnings,
                Notes = notes,
            },
        };

        return Finalize(artifact, state, errors, warnings);
    }

    
    
    

    private static byte[]? DecompressMam(byte[] compressed, List<PrefetchDiagnostic> errors)
    {
        if (compressed.Length < 8)
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Compression, "MAM header truncated"));
            return null;
        }

        uint sig = BitConverter.ToUInt32(compressed, 0);
        uint declaredSize = BitConverter.ToUInt32(compressed, 4);
        if (declaredSize is 0 or > MaxDecompressedPfBytes)
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Compression,
                $"MAM declares implausible uncompressed size {declaredSize}"));
            return null;
        }

        int primaryFormat = (int)((sig & 0x0F000000u) >> 24);
        int[] candidates =
        {
            primaryFormat,
            NativeMethods.COMPRESSION_FORMAT_XPRESS_HUFF,
            NativeMethods.COMPRESSION_FORMAT_XPRESS,
            NativeMethods.COMPRESSION_FORMAT_LZNT1,
            NativeMethods.COMPRESSION_FORMAT_DEFAULT,
        };

        var tried = new HashSet<int>();
        foreach (int format in candidates)
        {
            if (format is not (1 or 2 or 3 or 4) || !tried.Add(format))
                continue;
            byte[]? result = TryDecompress(compressed, (int)declaredSize, (ushort)format);
            if (result is not null)
                return result;
        }

        errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Compression,
            "All compression formats failed (bad payload, wrong declared size, or corrupt stream)"));
        return null;
    }

    private static byte[]? TryDecompress(byte[] compressed, int declaredSize, ushort format)
    {
        if (declaredSize <= 0 || declaredSize > MaxDecompressedPfBytes)
            return null;

        uint status = NativeMethods.RtlGetCompressionWorkSpaceSize(format, out uint compressWs, out uint fragmentWs);
        if (status != 0)
            return null;
        uint ws = Math.Max(compressWs, fragmentWs);
        if (ws == 0 || ws > MaxWorkspaceBytes)
            return null;

        byte[] payload = new byte[compressed.Length - 8];
        Buffer.BlockCopy(compressed, 8, payload, 0, payload.Length);

        byte[] output = new byte[declaredSize];
        IntPtr wsPtr = IntPtr.Zero;
        try
        {
            wsPtr = Marshal.AllocHGlobal((int)ws);
            status = NativeMethods.RtlDecompressBufferEx(
                format, output, output.Length, payload, payload.Length, out int finalSize, wsPtr);
            if (status != 0 || finalSize <= 0 || finalSize > output.Length)
                return null;
            if (finalSize != output.Length)
                Array.Resize(ref output, finalSize);
            return output;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
        finally
        {
            if (wsPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(wsPtr);
        }
    }

    private static string MamFormatName(byte[] data)
    {
        int format = (int)((BitConverter.ToUInt32(data, 0) & 0x0F000000u) >> 24);
        return format switch
        {
            1 => "LZNT1 (default)",
            2 => "LZNT1",
            3 => "XPRESS",
            4 => "XPRESS-HUFF",
            _ => $"unknown-{format}",
        };
    }

    
    
    

    private static string ExtractHeaderExecutableName(byte[] data)
    {
        int fieldEnd = Math.Min(PrefetchFormatProfile.OffExeName + PrefetchFormatProfile.ExeNameFieldBytes, data.Length);
        int end = fieldEnd;
        for (int i = PrefetchFormatProfile.OffExeName; i + 1 < fieldEnd; i += 2)
        {
            if (data[i] == 0 && data[i + 1] == 0)
            {
                end = i;
                break;
            }
        }
        if (end <= PrefetchFormatProfile.OffExeName)
            return "";
        try
        {
            return Encoding.Unicode.GetString(data, PrefetchFormatProfile.OffExeName, end - PrefetchFormatProfile.OffExeName).Trim();
        }
        catch
        {
            return "";
        }
    }

    private static string? ExtractHashString(byte[] data, PrefetchFormatProfile profile)
    {
        if (!profile.HasHashString)
            return null;
        int field = profile.HashStringFieldOffset;
        if (data.Length < field + 8)
            return null;
        int off = ReadI32(data, field);
        int size = ReadI32(data, field + 4);
        if (off <= 0x80 || size <= 0 || off + size > data.Length || size > 2048)
            return null;
        return DecodeUtf16(data, off, size);
    }

    private static SizeValidation ClassifySize(int declared, int actual, List<PrefetchDiagnostic> warnings)
    {
        if (declared <= 0 || declared < MinPfSize)
            return SizeValidation.Unverifiable;
        if (declared == actual)
            return SizeValidation.SizeMatches;
        if (declared > actual)
        {
            warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Integrity,
                $"Declared size {declared} exceeds actual {actual}"));
            return SizeValidation.Truncated;
        }
        warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Integrity,
            $"{actual - declared} trailing bytes after the declared end"));
        return SizeValidation.TrailingData;
    }

    
    
    

    private static int ReadRunCount(byte[] data, PrefetchFormatProfile profile, List<PrefetchDiagnostic> warnings)
    {
        int primary = SanitizeRunCount(ReadI32(data, profile.RunCountOffset));
        if (primary > 0)
            return primary;
        if (profile.RunCountFallbackOffset != 0)
        {
            int fallback = SanitizeRunCount(ReadI32(data, profile.RunCountFallbackOffset));
            if (fallback > 0)
            {
                warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Timestamp,
                    $"Run count at 0x{profile.RunCountOffset:X} implausible; recovered from 0x{profile.RunCountFallbackOffset:X}"));
                return fallback;
            }
        }
        return 0;
    }

    private static int SanitizeRunCount(int value)
        => value > 0 && value <= 1_000_000 ? value : 0;

    private static List<PrefetchExecutionTime> ReadExecutionTimes(byte[] data, PrefetchFormatProfile profile, List<PrefetchDiagnostic> warnings)
    {
        var times = new List<PrefetchExecutionTime>(8);
        int offset = profile.ExecTimeOffset;
        for (int i = 0; i < profile.ExecTimeCount; i++)
        {
            if (!PrefetchBounds.CanRead(offset, 8, data.Length))
                break;
            long li = BitConverter.ToInt64(data, offset);
            if (li != 0)
            {
                long unix = ForensicUtil.FileTimeToUnixTime((ulong)li);
                if (!ForensicUtil.IsPlausibleUnixTime(unix))
                {
                    warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Timestamp,
                        $"Execution time {i} (0x{offset:X}) is outside the plausible range"));
                    times.Add(new PrefetchExecutionTime
                    {
                        Index = i,
                        RawFileTime = (ulong)li,
                        UnixSeconds = 0,
                        Evidence = EvidenceLevel.Unknown,
                    });
                    offset += 8;
                    continue;
                }
                times.Add(new PrefetchExecutionTime
                {
                    Index = i,
                    RawFileTime = (ulong)li,
                    UnixSeconds = unix,
                    LocalTime = ForensicUtil.UnixTimeToLocalString(unix),
                    Evidence = EvidenceLevel.Exact,
                });
            }
            else
            {
                times.Add(new PrefetchExecutionTime
                {
                    Index = i,
                    RawFileTime = 0,
                    UnixSeconds = 0,
                    Evidence = EvidenceLevel.Unknown,
                });
            }
            offset += 8;
        }
        return times;
    }

    private static List<PrefetchFileString> ReadFileStrings(byte[] data, int stringsOffset, int stringsSize,
        PrefetchFormatProfile profile, List<PrefetchDiagnostic> errors, List<PrefetchDiagnostic> warnings)
    {
        var result = new List<PrefetchFileString>();
        if (stringsSize <= 0)
        {
            warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Bounds, "String table size is zero"));
            return result;
        }
        if (!PrefetchBounds.IsRangeValid(stringsOffset, stringsSize, data.Length))
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Bounds,
                $"String table [0x{stringsOffset:X}, +{stringsSize}) out of bounds"));
            return result;
        }

        int sizeBytes = Math.Min(stringsSize, 2 * 1024 * 1024);
        int limit = Math.Min(profile.MaxReferencedFiles, 4096);
        var current = new StringBuilder(64);
        int currentOffset = -1;

        void Flush()
        {
            if (current.Length == 0)
                return;
            if (result.Count < limit)
            {
                result.Add(new PrefetchFileString
                {
                    Index = result.Count,
                    Offset = currentOffset,
                    Length = current.Length * 2,
                    Value = current.ToString(),
                    Validation = StringValidation.Valid,
                });
            }
            current.Clear();
        }

        for (int pos = 0; pos + 2 <= sizeBytes; pos += 2)
        {
            char ch = (char)(data[stringsOffset + pos] | (data[stringsOffset + pos + 1] << 8));
            if (ch == '\0')
            {
                Flush();
                continue;
            }
            if (char.IsControl(ch) && ch != '\t')
            {
                
                
                if (current.Length == 0)
                    currentOffset = stringsOffset + pos;
                current.Append(ch);
                continue;
            }
            if (current.Length == 0)
                currentOffset = stringsOffset + pos;
            current.Append(ch);
        }
        if (current.Length > 0 && result.Count < limit)
        {
            result.Add(new PrefetchFileString
            {
                Index = result.Count,
                Offset = currentOffset,
                Length = current.Length * 2,
                Value = current.ToString(),
                Validation = StringValidation.NotNullTerminated,
            });
        }

        if (result.Count >= limit && stringsSize > 0)
            warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Bounds,
                $"String table exceeds the {limit}-string cap; {result.Count} retained"));
        return result;
    }

    private static List<PrefetchMetric> ReadMetrics(byte[] data, PrefetchFormatProfile profile,
        int metricsOffset, int metricsCount, List<PrefetchDiagnostic> errors, List<PrefetchDiagnostic> warnings)
    {
        var result = new List<PrefetchMetric>();
        
        
        
        if (metricsCount <= 0)
        {
            warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Metrics, "Metrics count is zero"));
            return result;
        }
        if (metricsCount > profile.MaxReferencedFiles)
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Metrics,
                $"Metrics count {metricsCount} exceeds the {profile.MaxReferencedFiles} cap"));
            return result;
        }
        if (!PrefetchBounds.IsArrayInBounds(metricsOffset, metricsCount, profile.MetricsEntrySize, data.Length))
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Metrics,
                $"Metrics array [0x{metricsOffset:X}, {metricsCount}×{profile.MetricsEntrySize}) out of bounds"));
            return result;
        }

        for (int i = 0; i < metricsCount; i++)
        {
            int e = metricsOffset + i * profile.MetricsEntrySize;
            ulong fileRef = 0;
            if (profile.MetricsFileRefOffset >= 0 && PrefetchBounds.CanRead(e + profile.MetricsFileRefOffset, 8, data.Length))
                fileRef = BitConverter.ToUInt64(data, e + profile.MetricsFileRefOffset);

            uint flags = 0;
            int chain = -1;
            if (profile.HasMetricsFlags)
            {
                if (profile.MetricsFlagsOffset >= 0 && PrefetchBounds.CanRead(e + profile.MetricsFlagsOffset, 4, data.Length))
                    flags = (uint)ReadI32(data, e + profile.MetricsFlagsOffset);
                if (profile.MetricsChainIndexOffset >= 0 && PrefetchBounds.CanRead(e + profile.MetricsChainIndexOffset, 4, data.Length))
                    chain = ReadI32(data, e + profile.MetricsChainIndexOffset);
            }

            result.Add(new PrefetchMetric
            {
                Index = i,
                Offset = e,
                Size = profile.MetricsEntrySize,
                FileReference = fileRef,
                MftEntry = (long)(fileRef & 0x0000_FFFF_FFFF_FFFFUL),
                MftSequence = (int)(fileRef >> 48),
                Flags = flags,
                LoadAsExecutable = (flags & MetricsFlagLoadAsExecutable) != 0,
                ChainIndex = chain,
            });
        }
        return result;
    }

    private const uint MetricsFlagLoadAsExecutable = 0x00000200;

    private static List<PrefetchTraceChain> ReadTraceChains(byte[] data, PrefetchFormatProfile profile,
        int chainsOffset, int chainsCount, List<PrefetchMetric> metrics,
        List<PrefetchDiagnostic> errors, List<PrefetchDiagnostic> warnings)
    {
        var result = new List<PrefetchTraceChain>();
        if (!profile.HasTraceChains)
            return result;
        if (chainsCount <= 0)
        {
            warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.TraceChain, "Trace-chain count is zero"));
            return result;
        }
        if (chainsCount > profile.MaxReferencedFiles || chainsOffset < PrefetchFormatProfile.HeaderRegionEnd)
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.TraceChain,
                $"Trace-chain table [0x{chainsOffset:X}, {chainsCount}×{profile.TraceChainEntrySize}) implausible"));
            return result;
        }
        if (!PrefetchBounds.IsArrayInBounds(chainsOffset, chainsCount, profile.TraceChainEntrySize, data.Length))
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.TraceChain,
                $"Trace-chain table [0x{chainsOffset:X}, {chainsCount}×{profile.TraceChainEntrySize}) out of bounds"));
            return result;
        }

        var depthByChain = new Dictionary<int, int>();
        foreach (var m in metrics)
        {
            if (m.ChainIndex >= 0 && m.ChainIndex < chainsCount)
                depthByChain[m.ChainIndex] = depthByChain.TryGetValue(m.ChainIndex, out var d) ? d + 1 : 1;
        }

        for (int i = 0; i < chainsCount; i++)
        {
            int e = chainsOffset + i * profile.TraceChainEntrySize;
            int blocks = PrefetchBounds.CanRead(e, 4, data.Length) ? ReadI32(data, e) : 0;
            int sanitized = blocks > 0 && blocks < 1_000_000 ? blocks : 0;
            depthByChain.TryGetValue(i, out int depth);
            result.Add(new PrefetchTraceChain
            {
                Index = i,
                Offset = e,
                EntrySize = profile.TraceChainEntrySize,
                BlockLoads = sanitized,
                Depth = depth,
                MetricsReference = depth > 0 ? i + 1 : 0,
            });
        }
        return result;
    }

    private static (List<PrefetchVolume> Volumes, List<string> Directories) ReadVolumes(byte[] data,
        PrefetchFormatProfile profile, int volumesOffset, int volumesCount,
        List<PrefetchDiagnostic> errors, List<PrefetchDiagnostic> warnings)
    {
        var volumes = new List<PrefetchVolume>();
        var directories = new List<string>();

        int count = SanitizeSmallCount(volumesCount);
        if (count == 0)
        {
            warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Volume, "Volume count is zero"));
            return (volumes, directories);
        }
        if (volumesOffset <= 0 || volumesOffset >= data.Length)
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Volume,
                $"Volume table offset 0x{volumesOffset:X} invalid"));
            return (volumes, directories);
        }
        if (!PrefetchBounds.IsArrayInBounds(volumesOffset, count, profile.VolumeEntrySize, data.Length))
        {
            errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Volume,
                $"Volume table [0x{volumesOffset:X}, {count}×{profile.VolumeEntrySize}) out of bounds"));
            return (volumes, directories);
        }

        int dirCount = 0;
        for (int i = 0; i < count; i++)
        {
            int e = volumesOffset + i * profile.VolumeEntrySize;
            if (!PrefetchBounds.CanRead(e, 36, data.Length))
            {
                warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Volume,
                    $"Volume entry {i} at 0x{e:X} truncated"));
                break;
            }

            uint serial = (uint)ReadI32(data, e + profile.VolumeSerialOffset);
            string devicePath = "";
            int pathOffset = ReadI32(data, e + profile.VolumeDevicePathOffset);
            int pathChars = ReadI32(data, e + profile.VolumeDevicePathCharsOffset);
            if (pathChars > 0 && pathChars <= 1024 && pathOffset >= 0)
            {
                int pp = volumesOffset + pathOffset;
                if (PrefetchBounds.IsRangeValid(pp, pathChars * 2, data.Length))
                    devicePath = DecodeUtf16(data, pp, pathChars * 2);
            }

            var dirs = new List<string>(4);
            int dirsOffset = ReadI32(data, e + profile.VolumeDirsOffset);
            int dirsCount = ReadI32(data, e + profile.VolumeDirsCountOffset);
            if (dirsCount > 0 && dirsCount <= 4096 && dirsOffset >= 0)
            {
                int ds = volumesOffset + dirsOffset;
                int parsed = 0;
                while (parsed < dirsCount && parsed < 1024 && PrefetchBounds.CanRead(ds, 2, data.Length))
                {
                    int slen = ReadU16(data, ds);
                    ds += 2;
                    if (slen <= 0 || slen > 1024 || !PrefetchBounds.IsRangeValid(ds, slen * 2, data.Length))
                    {
                        warnings.Add(new PrefetchDiagnostic(PrefetchErrorCode.Volume,
                            $"Directory string {parsed} of volume {i} invalid at 0x{ds - 2:X}"));
                        break;
                    }
                    string s = DecodeUtf16(data, ds, slen * 2);
                    ds += slen * 2;
                    if (s.Length > 0)
                    {
                        dirs.Add(s);
                        if (!directories.Contains(s))
                            directories.Add(s);
                    }
                    parsed++;
                    if (PrefetchBounds.CanRead(ds, 2, data.Length))
                        ds += 2; 
                }
                dirCount += parsed;
            }

            volumes.Add(new PrefetchVolume
            {
                Index = i,
                Offset = e,
                EntrySize = profile.VolumeEntrySize,
                Serial = serial,
                DevicePath = devicePath,
                DirectoryStrings = dirs,
                Evidence = EvidenceLevel.Exact,
            });
        }
        _ = dirCount;
        return (volumes, directories);
    }

    private static int SanitizeSmallCount(int value)
        => value is > 0 and <= 256 ? value : 0;

    
    
    

    private static PrefetchIdentity BuildIdentity(PrefetchFilename filename, string headerExe,
        uint storedHash, string? hashString, int version, bool isBoot)
    {
        IdentityState exeState;
        string? renamedNote = null;
        if (string.IsNullOrEmpty(filename.ExecutableName) || string.IsNullOrEmpty(headerExe))
        {
            exeState = IdentityState.Unverifiable;
        }
        else if (PrefetchFilenameParser.IsRenamed(filename.ExecutableName, headerExe))
        {
            exeState = IdentityState.Mismatch;
            renamedNote = $"PF filename executable \"{filename.ExecutableName}\" differs from header executable \"{headerExe}\"";
        }
        else
        {
            exeState = isBoot ? IdentityState.Ambiguous : IdentityState.Match;
        }

        IdentityState filenameHashState = IdentityState.Unverifiable;
        if (filename.FilenameHash is { } filenameHash && storedHash != 0)
            filenameHashState = filenameHash == storedHash ? IdentityState.Match : IdentityState.Mismatch;

        IdentityState storedVsHashString = IdentityState.Unverifiable;
        if (version is 30 or 31 && storedHash != 0 && !string.IsNullOrWhiteSpace(hashString) &&
            hashString.StartsWith(@"\DEVICE\", StringComparison.OrdinalIgnoreCase))
        {
            storedVsHashString = PrefetchHash.IsStoredHashValid(storedHash, hashString, version)
                ? IdentityState.Match
                : IdentityState.Mismatch;
        }

        return new PrefetchIdentity
        {
            FilenameExecutable = filename.ExecutableName,
            FilenameHash = filename.FilenameHash,
            FilenameExeVsHeader = exeState,
            FilenameHashVsStored = filenameHashState,
            StoredHashVsHashString = storedVsHashString,
            RenamedNote = renamedNote,
        };
    }

    
    
    

    private static List<PrefetchRegion> BuildRegions(int actualSize, int declaredSize, SizeValidation sizeState,
        int metricsOffset, int metricsCount, PrefetchFormatProfile profile,
        int chainsOffset, int chainsCount, PrefetchFormatProfile _profile2,
        int stringsOffset, int stringsSize,
        int volumesOffset, int volumesCount, PrefetchFormatProfile _profile3)
    {
        var regions = new List<PrefetchRegion>
        {
            new() { Type = PrefetchRegionType.Header, Offset = 0, Length = PrefetchFormatProfile.HeaderRegionEnd, Confidence = 100 },
        };

        if (metricsCount > 0 && PrefetchBounds.IsArrayInBounds(metricsOffset, metricsCount, profile.MetricsEntrySize, actualSize))
            regions.Add(new PrefetchRegion { Type = PrefetchRegionType.Metrics, Offset = metricsOffset, Length = metricsCount * profile.MetricsEntrySize, Confidence = 100 });

        if (profile.HasTraceChains && chainsCount > 0 &&
            PrefetchBounds.IsArrayInBounds(chainsOffset, chainsCount, profile.TraceChainEntrySize, actualSize))
            regions.Add(new PrefetchRegion { Type = PrefetchRegionType.TraceChains, Offset = chainsOffset, Length = chainsCount * profile.TraceChainEntrySize, Confidence = 100 });

        if (stringsSize > 0 && PrefetchBounds.IsRangeValid(stringsOffset, stringsSize, actualSize))
            regions.Add(new PrefetchRegion { Type = PrefetchRegionType.FileStrings, Offset = stringsOffset, Length = stringsSize, Confidence = 100 });

        if (volumesCount > 0 && PrefetchBounds.IsArrayInBounds(volumesOffset, volumesCount, profile.VolumeEntrySize, actualSize))
            regions.Add(new PrefetchRegion { Type = PrefetchRegionType.VolumeInformation, Offset = volumesOffset, Length = volumesCount * profile.VolumeEntrySize, Confidence = 100 });

        if (sizeState == SizeValidation.TrailingData && declaredSize >= MinPfSize && declaredSize < actualSize)
            regions.Add(new PrefetchRegion { Type = PrefetchRegionType.TrailingData, Offset = declaredSize, Length = actualSize - declaredSize, Confidence = 100 });

        return regions;
    }

    private static List<PrefetchRegionOverlap> DetectOverlaps(List<PrefetchRegion> regions, PrefetchFormatProfile profile)
    {
        var overlaps = new List<PrefetchRegionOverlap>();
        var sections = regions
            .Where(r => r.Type is PrefetchRegionType.Metrics or PrefetchRegionType.TraceChains
                or PrefetchRegionType.FileStrings or PrefetchRegionType.VolumeInformation)
            .ToList();
        for (int i = 0; i < sections.Count; i++)
        {
            for (int j = i + 1; j < sections.Count; j++)
            {
                var a = sections[i];
                var b = sections[j];
                int start = Math.Max(a.Offset, b.Offset);
                int end = Math.Min(a.Offset + a.Length, b.Offset + b.Length);
                if (end > start)
                {
                    overlaps.Add(new PrefetchRegionOverlap
                    {
                        First = a.Type,
                        Second = b.Type,
                        OverlapBytes = end - start,
                    });
                }
            }
        }
        return overlaps;
    }

    private static List<PrefetchUnknownRegion> FindUnknownRegions(byte[] data, List<PrefetchRegion> regions,
        SizeValidation sizeState, List<PrefetchDiagnostic> warnings)
    {
        var result = new List<PrefetchUnknownRegion>();
        var sorted = regions.OrderBy(r => r.Offset).ToList();
        int cursor = 0;
        int gapStart = -1;
        foreach (var region in sorted)
        {
            if (region.Offset > cursor)
            {
                if (gapStart < 0)
                    gapStart = cursor;
                int gapLen = region.Offset - cursor;
                if (gapLen > 0 && result.Count < MaxUnknownRegions)
                    result.Add(MakeUnknownRegion(data, cursor, gapLen));
            }
            cursor = Math.Max(cursor, region.Offset + region.Length);
            gapStart = -1;
        }
        
        if (sizeState != SizeValidation.TrailingData && cursor < data.Length && result.Count < MaxUnknownRegions)
            result.Add(MakeUnknownRegion(data, cursor, data.Length - cursor));
        return result;
    }

    private static PrefetchUnknownRegion MakeUnknownRegion(byte[] data, int offset, int length)
    {
        int previewLen = Math.Min(length, UnknownRegionPreviewBytes);
        var preview = Convert.ToHexString(data, offset, previewLen);
        return new PrefetchUnknownRegion
        {
            Offset = offset,
            Length = length,
            HexPreview = preview,
            Entropy = Entropy(data, offset, length),
        };
    }

    private static double Entropy(byte[] data, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset >= data.Length)
            return 0;
        long end = Math.Min((long)offset + length, data.Length);
        long total = end - offset;
        const int cap = 4096;
        long step = total > cap ? total / cap : 1;
        int[] counts = new int[256];
        long sampled = 0;
        for (long i = offset; i < end; i += step)
        {
            counts[data[i]]++;
            sampled++;
        }
        if (sampled == 0)
            return 0;
        double e = 0;
        for (int i = 0; i < 256; i++)
        {
            if (counts[i] == 0)
                continue;
            double p = (double)counts[i] / sampled;
            e -= p * Math.Log2(p);
        }
        return Math.Round(e, 2);
    }

    
    
    

    private static PrefetchParseResult UnknownVersion(byte[] data, int version, string? sourcePath,
        List<PrefetchDiagnostic> errors, List<PrefetchDiagnostic> warnings, List<PrefetchDiagnostic> notes)
    {
        errors.Add(new PrefetchDiagnostic(PrefetchErrorCode.Version, $"Unsupported prefetch format version {version}"));
        var filename = PrefetchFilenameParser.Parse(string.IsNullOrEmpty(sourcePath) ? "" : Path.GetFileName(sourcePath));
        var artifact = new PrefetchArtifact
        {
            Kind = Identify(data) == PrefetchKind.Mam ? PrefetchKind.Mam : PrefetchKind.Scca,
            State = PrefetchParseState.UnknownVersion,
            Confidence = PrefetchConfidence.FromState(PrefetchParseState.UnknownVersion),
            FormatVersion = version,
            VersionName = $"Unknown ({version})",
            Variant = PrefetchVariant.Default,
            Support = SupportLevel.Unknown,
            SccaMagic = ReadI32(data, PrefetchFormatProfile.OffSccaMagic),
            DeclaredFileSize = ReadI32(data, PrefetchFormatProfile.OffDeclaredSize),
            HeaderExecutableName = ExtractHeaderExecutableName(data),
            StoredPrefetchHash = (uint)ReadI32(data, PrefetchFormatProfile.OffPrefetchHash),
            Identity = new PrefetchIdentity { FilenameExecutable = filename.ExecutableName, FilenameHash = filename.FilenameHash },
            Validation = new PrefetchValidation
            {
                State = PrefetchParseState.UnknownVersion,
                Errors = errors,
                Warnings = warnings,
                Notes = notes,
            },
        };
        return Finalize(artifact, PrefetchParseState.UnknownVersion, errors, warnings);
    }

    private static PrefetchParseResult Finalize(PrefetchArtifact? artifact, PrefetchParseState state,
        List<PrefetchDiagnostic> errors, List<PrefetchDiagnostic> warnings)
    {
        
        
        
        var result = new PrefetchParseResult { Artifact = artifact, State = state };
        result.Errors.AddRange(errors);
        result.Warnings.AddRange(warnings);
        return result;
    }

    
    
    

    private static int ReadI32(byte[] data, int offset)
    {
        if ((uint)offset > data.Length - 4u)
            return 0;
        return BitConverter.ToInt32(data, offset);
    }

    private static int ReadU16(byte[] data, int offset)
    {
        if ((uint)offset > data.Length - 2u)
            return 0;
        return BitConverter.ToUInt16(data, offset);
    }

    private static string DecodeUtf16(byte[] data, int offset, int byteLen)
    {
        int end = offset + (byteLen & ~1);
        if (end > data.Length)
            end = data.Length & ~1;
        for (int i = offset; i + 1 < end; i += 2)
        {
            if (data[i] == 0 && data[i + 1] == 0)
            {
                end = i;
                break;
            }
        }
        if (end <= offset)
            return "";
        try
        {
            return Encoding.Unicode.GetString(data, offset, end - offset);
        }
        catch
        {
            return "";
        }
    }
}
