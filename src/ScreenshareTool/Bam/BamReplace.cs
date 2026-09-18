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

    private static readonly string[][] ExplorerPattern =
    {
        new[] { "File delete", "Close" },
        new[] { "Rename: old name" },
        new[] { "Rename: new name" },
        new[] { "Rename: new name", "Close" }
    };

    private static readonly string[][] CopyPatternA =
    {
        new[] { "Data truncation", "Security change" },
        new[] { "Data extend", "Data truncation", "Security change" },
        new[] { "Data overwrite", "Data extend", "Data truncation", "Security change" },
        new[] { "Data overwrite", "Data extend", "Data truncation", "Security change", "Basic info change" },
        new[] { "Data overwrite", "Data extend", "Data truncation", "Security change", "Basic info change", "Close" }
    };

    private static readonly string[][] CopyPatternB =
    {
        new[] { "Data truncation" },
        new[] { "Data extend", "Data truncation" },
        new[] { "Data overwrite", "Data extend", "Data truncation" },
        new[] { "Data overwrite", "Data extend", "Data truncation", "Basic info change" },
        new[] { "Data overwrite", "Data extend", "Data truncation", "Basic info change", "Close" }
    };

    private static readonly string[][] TypePatternA =
    {
        new[] { "Data extend", "Data truncation" },
        new[] { "Data extend", "Data truncation", "Close" }
    };

    private static readonly string[][] TypePatternB =
    {
        new[] { "Data truncation" },
        new[] { "Data extend", "Data truncation" }
    };

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

                // Reason texts are pure functions of the reason bits; format
                // once per record instead of once per detector window.
                var texts = new string[records.Count];
                for (int t = 0; t < records.Count; t++)
                {
                    ct.ThrowIfCancellationRequested();
                    texts[t] = BamUsn.ReasonText(records[t].Reason);
                }

                DetectExplorer(records, texts, cache);
                ct.ThrowIfCancellationRequested();
                DetectCopy(records, texts, cache);
                ct.ThrowIfCancellationRequested();
                DetectType(records, texts, cache);
                ct.ThrowIfCancellationRequested();
                DetectDelete(records, texts, cache);
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
        if (cache.TryGetValue(name.ToLowerInvariant(), out var hits))
            return hits;
        return new List<Hit>();
    }

    private static void AddHit(
        Dictionary<string, List<Hit>> cache, string name, string type, string details)
    {
        string key = name.ToLowerInvariant();
        if (!cache.TryGetValue(key, out var list))
        {
            list = new List<Hit>();
            cache[key] = list;
        }
        list.Add(new Hit { Filename = name, ReplaceType = type, Details = details });
    }

    private static bool CheckPattern(List<string> reasons, string[][] pattern)
    {
        if (reasons.Count != pattern.Length)
            return false;
        for (int i = 0; i < pattern.Length; i++)
        {
            foreach (string term in pattern[i])
            {
                if (!reasons[i].Contains(term, StringComparison.Ordinal))
                    return false;
            }
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

    private static string WindowDetails(List<BamUsn.Record> entries, string[] texts, int start, int length)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < length; i++)
        {
            var rec = entries[start + i];
            string time;
            try
            {
                time = DateTime.FromFileTime(rec.TimeStamp).ToString("dd/MM/yyyy HH:mm:ss");
            }
            catch
            {
                time = "";
            }
            sb.Append("USN: ").Append(rec.TimeStamp).Append('\n');
            sb.Append("name: ").Append(rec.Name).Append('\n');
            sb.Append("time: ").Append(time).Append('\n');
            sb.Append("reason: ").Append(texts[start + i]).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static void DetectExplorer(
        List<BamUsn.Record> entries, string[] texts, Dictionary<string, List<Hit>> cache)
    {
        for (int i = 0; i < entries.Count - 3; i++)
        {
            var reasons = new List<string>(4)
            {
                texts[i], texts[i + 1], texts[i + 2], texts[i + 3]
            };
            if (CheckPattern(reasons, ExplorerPattern) && AllSameName(entries, i, 4))
                AddHit(cache, entries[i].Name, "Explorer", WindowDetails(entries, texts, i, 4));
        }
    }

    private static void DetectCopy(
        List<BamUsn.Record> entries, string[] texts, Dictionary<string, List<Hit>> cache)
    {
        for (int i = 0; i <= entries.Count - 5; i++)
        {
            if (!AllSameName(entries, i, 5))
                continue;
            var reasons = new List<string>(5);
            for (int k = 0; k < 5; k++)
                reasons.Add(texts[i + k]);
            if (CheckPattern(reasons, CopyPatternA) || CheckPattern(reasons, CopyPatternB))
                AddHit(cache, entries[i].Name, "Copy", WindowDetails(entries, texts, i, 5));
        }
    }

    private static void DetectType(
        List<BamUsn.Record> entries, string[] texts, Dictionary<string, List<Hit>> cache)
    {
        for (int i = 0; i < entries.Count - 1; i++)
        {
            if (!AllSameName(entries, i, 2))
                continue;
            var reasons = new List<string>(2) { texts[i], texts[i + 1] };
            if (CheckPattern(reasons, TypePatternA) || CheckPattern(reasons, TypePatternB))
                AddHit(cache, entries[i].Name, "Type", WindowDetails(entries, texts, i, 2));
        }
    }

    private static void DetectDelete(
        List<BamUsn.Record> entries, string[] texts, Dictionary<string, List<Hit>> cache)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (texts[i].Contains("File delete", StringComparison.Ordinal) &&
                texts[i].Contains("Close", StringComparison.Ordinal))
            {
                AddHit(cache, entries[i].Name,
                    "Delete", WindowDetails(entries, texts, i, 1));
            }
        }
    }
}
