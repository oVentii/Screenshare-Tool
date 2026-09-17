using System.Collections.Concurrent;
using System.IO;










internal sealed class ArtifactPathIndex
{
    private readonly ConcurrentDictionary<string, byte> _full = new(StringComparer.Ordinal);

    public int Count => _full.Count;

    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || ForensicUtil.IsUnresolved(path))
            return;

        foreach (string alias in Aliases(path))
            _full.TryAdd(alias, 0);
    }

    public bool Contains(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || ForensicUtil.IsUnresolved(path))
            return false;
        foreach (string alias in Aliases(path))
        {
            if (_full.ContainsKey(alias))
                return true;
        }
        return false;
    }

    public static IEnumerable<string> Aliases(string path)
    {
        string dos = VolumeMapper.ToDosPath(path);
        string n = ForensicUtil.NormalizePath(dos);
        if (n.Length == 0)
            yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal) { n };
        yield return n;

        foreach (string variant in Wow64Variants(n))
        {
            if (seen.Add(variant))
                yield return variant;
        }
    }

    private static IEnumerable<string> Wow64Variants(string n)
    {
        if (n.Contains(@"\windows\system32\", StringComparison.Ordinal))
        {
            yield return n.Replace(@"\windows\system32\", @"\windows\syswow64\", StringComparison.Ordinal);
            yield return n.Replace(@"\windows\system32\", @"\windows\sysnative\", StringComparison.Ordinal);
        }
        if (n.Contains(@"\windows\syswow64\", StringComparison.Ordinal))
        {
            yield return n.Replace(@"\windows\syswow64\", @"\windows\system32\", StringComparison.Ordinal);
            yield return n.Replace(@"\windows\syswow64\", @"\windows\sysnative\", StringComparison.Ordinal);
        }
        if (n.Contains(@"\program files (x86)\", StringComparison.Ordinal))
            yield return n.Replace(@"\program files (x86)\", @"\program files\", StringComparison.Ordinal);
        else if (n.Contains(@"\program files\", StringComparison.Ordinal) &&
                 !n.Contains(@"\program files (x86)\", StringComparison.Ordinal))
            yield return n.Replace(@"\program files\", @"\program files (x86)\", StringComparison.Ordinal);
    }
}
