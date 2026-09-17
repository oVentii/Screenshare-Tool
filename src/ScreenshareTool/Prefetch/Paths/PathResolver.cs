using System.IO;







internal static class PathResolver
{
    public sealed class Result
    {
        public string Path { get; init; } = ForensicUtil.UnresolvedPath;
        public bool Exists { get; init; }
        public string ResolvedFrom { get; init; } = "";
        public string? LastSeenPath { get; init; }
        public string? LastSeenSource { get; init; }
        public string? LastSeenTime { get; init; }
        public string? Publisher { get; init; }
    }

    public static Result Resolve(
        string exeNameFromPf,
        string candidateFromPf,
        PrefetchParser.RiskContext? context)
    {
        var last = FindLastSeen(exeNameFromPf, candidateFromPf, context);

        string? existing = TryExisting(candidateFromPf);
        if (existing is not null)
            return Finish(existing, exists: true, "Prefetch", last);

        if (last is not null)
        {
            existing = TryExisting(last.Path);
            if (existing is not null)
                return Finish(existing, exists: true, last.Source, last);
        }

        existing = TryCommonInstallDirs(exeNameFromPf);
        if (existing is not null)
            return Finish(existing, exists: true, "CommonDir", last);

        string remembered = !ForensicUtil.IsUnresolved(candidateFromPf)
            ? VolumeMapper.ToDosPath(candidateFromPf)
            : last?.Path ?? ForensicUtil.UnresolvedPath;

        return new Result
        {
            Path = string.IsNullOrEmpty(remembered) ? ForensicUtil.UnresolvedPath : remembered,
            Exists = false,
            ResolvedFrom = last is not null ? last.Source : (ForensicUtil.IsUnresolved(candidateFromPf) ? "" : "Prefetch"),
            LastSeenPath = last?.Path,
            LastSeenSource = last?.Source,
            LastSeenTime = last?.Timestamp,
            Publisher = last?.Publisher
        };
    }

    private static Result Finish(string existing, bool exists, string from, CacheHint? last)
        => new()
        {
            Path = existing,
            Exists = exists,
            ResolvedFrom = from,
            LastSeenPath = last?.Path,
            LastSeenSource = last?.Source,
            LastSeenTime = last?.Timestamp,
            Publisher = last?.Publisher
        };

    private static string? TryExisting(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || ForensicUtil.IsUnresolved(path))
            return null;

        string dos = VolumeMapper.ToDosPath(path);
        if (ForensicUtil.FileExistsNative(dos))
            return ForensicUtil.ResolveExistingPath(dos);

        foreach (string alias in ArtifactPathIndex.Aliases(dos))
        {
            if (ForensicUtil.FileExistsNative(alias))
                return ForensicUtil.ResolveExistingPath(alias);
        }

        return null;
    }

    private static string? TryCommonInstallDirs(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName))
            return null;

        var names = new List<string>(2) { exeName };
        if (!exeName.Contains('.', StringComparison.Ordinal))
        {
            names.Add(exeName + ".exe");
            names.Add(exeName + ".EXE");
        }

        var dirs = new List<string>(8);
        string win = ForensicUtil.GetWindowsDirectory();
        dirs.Add(Path.Combine(win, "System32"));
        dirs.Add(Path.Combine(win, "SysWOW64"));
        if (ForensicUtil.IsWow64Process)
            dirs.Add(Path.Combine(win, "Sysnative"));
        dirs.Add(win);
        string pf = ForensicUtil.GetProgramFiles();
        if (!string.IsNullOrEmpty(pf)) dirs.Add(pf);
        string x86 = ForensicUtil.GetProgramFilesX86();
        if (!string.IsNullOrEmpty(x86)) dirs.Add(x86);

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string trimmed = dir.Trim('"');
                if (trimmed.Length > 0)
                    dirs.Add(trimmed);
            }
        }

        foreach (string dir in dirs)
        {
            foreach (string name in names)
            {
                try
                {
                    string candidate = Path.Combine(dir, name);
                    if (candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                        continue;
                    if (ForensicUtil.FileExistsNative(candidate))
                        return ForensicUtil.ResolveExistingPath(candidate);
                }
                catch
                {
                    
                }
            }
        }

        return null;
    }

    private static CacheHint? FindLastSeen(
        string exeName, string candidateFromPf, PrefetchParser.RiskContext? context)
    {
        if (context is null)
            return null;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(exeName))
        {
            names.Add(exeName);
            names.Add(Path.GetFileName(exeName));
            if (!exeName.Contains('.', StringComparison.Ordinal))
                names.Add(exeName + ".exe");
        }

        if (!ForensicUtil.IsUnresolved(candidateFromPf) && !string.IsNullOrEmpty(candidateFromPf))
            names.Add(Path.GetFileName(candidateFromPf));

        CacheHint? best = null;
        foreach (string name in names)
        {
            if (name.Length == 0)
                continue;
            foreach (var hit in context.HitsForFileName(name))
            {
                if (best is null)
                    best = hit;
                else if (hit.Source == "Amcache" && best.Source != "Amcache")
                    best = hit;
            }
        }

        return best;
    }

    public sealed class CacheHint
    {
        public string Path { get; init; } = "";
        public string Source { get; init; } = "";
        public string? Timestamp { get; init; }
        public string? Publisher { get; init; }
    }
}
