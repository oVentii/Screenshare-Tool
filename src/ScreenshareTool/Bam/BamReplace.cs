using System.IO;
using System.Text;

internal static class BamReplace
{
    public sealed class Hit
    {
        public string Filename { get; init; } = "";
        public string ReplaceType { get; init; } = "";
        public string Details { get; init; } = "";
    }

    private const uint R_OVERWRITE = 0x1;
    private const uint R_EXTEND = 0x2;
    private const uint R_TRUNCATION = 0x4;
    private const uint R_SECURITY = 0x800;
    private const uint R_DELETE = 0x200;
    private const uint R_RENAME_OLD = 0x1000;
    private const uint R_RENAME_NEW = 0x2000;
    private const uint R_BASIC = 0x8000;
    private const uint R_CLOSE = 0x80000000u;

    private static readonly uint[] ExplorerMask = { R_DELETE | R_CLOSE, R_RENAME_OLD, R_RENAME_NEW, R_RENAME_NEW | R_CLOSE };
    private static readonly uint[] CopyMaskA =
    {
        R_TRUNCATION | R_SECURITY,
        R_EXTEND | R_TRUNCATION | R_SECURITY,
        R_OVERWRITE | R_EXTEND | R_TRUNCATION | R_SECURITY,
        R_OVERWRITE | R_EXTEND | R_TRUNCATION | R_SECURITY | R_BASIC,
        R_OVERWRITE | R_EXTEND | R_TRUNCATION | R_SECURITY | R_BASIC | R_CLOSE
    };
    private static readonly uint[] CopyMaskB =
    {
        R_TRUNCATION,
        R_EXTEND | R_TRUNCATION,
        R_OVERWRITE | R_EXTEND | R_TRUNCATION,
        R_OVERWRITE | R_EXTEND | R_TRUNCATION | R_BASIC,
        R_OVERWRITE | R_EXTEND | R_TRUNCATION | R_BASIC | R_CLOSE
    };
    private static readonly uint[] TypeMaskA = { R_EXTEND | R_TRUNCATION, R_EXTEND | R_TRUNCATION | R_CLOSE };
    private static readonly uint[] TypeMaskB = { R_TRUNCATION, R_EXTEND | R_TRUNCATION };

    public static (Dictionary<string, List<Hit>> Cache, int DrivesScanned, int DrivesSkipped) ScanDrives(CancellationToken ct)
    {
        var cache = new Dictionary<string, List<Hit>>(StringComparer.OrdinalIgnoreCase);
        int drivesScanned = 0;
        int drivesSkipped = 0;

        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;
                if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    drivesSkipped++;
                    continue;
                }

                var records = BamUsn.ReadDriveJournal(drive.Name, ct, out bool ok);
                if (!ok)
                {
                    drivesSkipped++;
                    continue;
                }
                drivesScanned++;

                DetectExplorer(records, cache, ct);
                ct.ThrowIfCancellationRequested();
                DetectCopy(records, cache, ct);
                ct.ThrowIfCancellationRequested();
                DetectType(records, cache, ct);
                ct.ThrowIfCancellationRequested();
                DetectDelete(records, cache, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        return (cache, drivesScanned, drivesSkipped);
    }

    public static List<Hit> Lookup(
        Dictionary<string, List<Hit>> cache, string filePathOrName)
    {
        string name = filePathOrName;
        int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (slash >= 0 && slash + 1 < name.Length)
            name = name.Substring(slash + 1);
        if (cache.TryGetValue(name, out var hits))
            return hits;
        return new List<Hit>();
    }

    private static void AddHit(
        Dictionary<string, List<Hit>> cache, string name, string type, string details)
    {
        if (!cache.TryGetValue(name, out var list))
        {
            list = new List<Hit>();
            cache[name] = list;
        }
        list.Add(new Hit { Filename = name, ReplaceType = type, Details = details });
    }

    private static bool MaskMatches(uint reason, uint mask) => (reason & mask) == mask;

    private static bool WindowMatches(List<BamUsn.Record> entries, int start, uint[] mask)
    {
        for (int k = 0; k < mask.Length; k++)
        {
            if (!MaskMatches(entries[start + k].Reason, mask[k]))
                return false;
        }
        return true;
    }

    private static bool AllSameName(List<BamUsn.Record> entries, int start, int count)
    {
        string name = entries[start].Name;
        for (int i = 1; i < count; i++)
        {
            if (!string.Equals(entries[start + i].Name, name, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static string WindowDetails(List<BamUsn.Record> entries, int start, int length)
    {
        var sb = new StringBuilder(length * 128);
        for (int i = 0; i < length; i++)
        {
            var rec = entries[start + i];
            string time;
            try
            {
                time = DateTime.FromFileTimeUtc(rec.TimeStamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                time = "";
            }
            sb.Append("timestamp: ").Append(rec.TimeStamp).Append('\n');
            sb.Append("name: ").Append(rec.Name).Append('\n');
            sb.Append("time: ").Append(time).Append('\n');
            sb.Append("reason: ").Append(BamUsn.ReasonText(rec.Reason)).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static void DetectExplorer(
        List<BamUsn.Record> entries, Dictionary<string, List<Hit>> cache, CancellationToken ct)
    {
        for (int i = 0; i + 4 <= entries.Count; i++)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (!AllSameName(entries, i, 4))
                continue;
            if (WindowMatches(entries, i, ExplorerMask))
                AddHit(cache, entries[i].Name, "Explorer", WindowDetails(entries, i, 4));
        }
    }

    private static void DetectCopy(
        List<BamUsn.Record> entries, Dictionary<string, List<Hit>> cache, CancellationToken ct)
    {
        for (int i = 0; i + 5 <= entries.Count; i++)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (!AllSameName(entries, i, 5))
                continue;
            if (WindowMatches(entries, i, CopyMaskA) || WindowMatches(entries, i, CopyMaskB))
                AddHit(cache, entries[i].Name, "Copy", WindowDetails(entries, i, 5));
        }
    }

    private static void DetectType(
        List<BamUsn.Record> entries, Dictionary<string, List<Hit>> cache, CancellationToken ct)
    {
        for (int i = 0; i + 2 <= entries.Count; i++)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (!AllSameName(entries, i, 2))
                continue;
            if (WindowMatches(entries, i, TypeMaskA) || WindowMatches(entries, i, TypeMaskB))
                AddHit(cache, entries[i].Name, "Type", WindowDetails(entries, i, 2));
        }
    }

    private static void DetectDelete(
        List<BamUsn.Record> entries, Dictionary<string, List<Hit>> cache, CancellationToken ct)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            uint r = entries[i].Reason;
            if (MaskMatches(r, R_DELETE | R_CLOSE))
                AddHit(cache, entries[i].Name, "Delete", WindowDetails(entries, i, 1));
        }
    }
}
