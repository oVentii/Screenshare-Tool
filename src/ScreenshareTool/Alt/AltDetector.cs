using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

public sealed class AltDetectorScanner
{
    private readonly object _lock = new();
    private readonly HashSet<string> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _discordIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _discordAccounts = new(StringComparer.Ordinal);

    private static readonly HashSet<string> UsernameBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts", "forge", "fabric", "vanilla", "profile", "instance", "optifine"
    };

    private static readonly string CacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "iRis-Screenshare-Tool", "alt-cache.txt");

    private int _cachedMcCount, _cachedDcCount;
    private int _launcherFiles, _logFiles, _discordDirs, _browserDirs;

    private static readonly string[] LauncherFiles =
    {
        @"AppData\Roaming\PrismLauncher\accounts.json",
        @"AppData\Roaming\.minecraft\labymod-neo\accounts.json",
        @"AppData\Roaming\.minecraft\launcher_accounts_microsoft_store.json",
        @"AppData\Roaming\.minecraft\LabyMod\accounts.json",
        @"AppData\Roaming\MultiMC\accounts.json",
        @"AppData\Roaming\.tlauncher\accounts.json",
        @"AppData\Roaming\.minecraft\BLClient\accounts.json",
        @".lunarclient\settings\game\accounts.json",
        @".lunarclient\settings\game-backup\accounts.json",
        @"AppData\Roaming\.feather\accounts.json",
        @"AppData\Roaming\.minecraft\Feather\accounts.json",
        @"AppData\Roaming\ATLauncher\accounts.json",
        @"AppData\Roaming\.minecraft\ATLauncher\accounts.json",
        @"AppData\Roaming\gdlauncher_next\accounts.json",
        @"AppData\Roaming\gdlauncher\accounts.json",
        @"AppData\Roaming\.minecraft\SKlauncher\accounts.json",
        @"AppData\Roaming\PolyMC\accounts.json",
        @"AppData\Roaming\.technic\accounts.json",
        @"AppData\Roaming\.minecraft\Technic\accounts.json",
        @"AppData\Roaming\crystal-launcher\accounts.json",
        @"AppData\Roaming\.minecraft\Crystal\accounts.json",
        @"AppData\Roaming\Valhalla\accounts.json",
        @"AppData\Roaming\.minecraft\ModrinthApp\accounts.json",
        @"AppData\Roaming\ModrinthApp\accounts.json",
        @"curseforge\minecraft\Install\accounts.json",
        @"AppData\Roaming\CurseForge\accounts.json",
        @"AppData\Local\CurseForge\accounts.json",
        @".minecraft\launcher_accounts.json",
        @"Documents\.minecraft\accounts.json",
        @"AppData\Roaming\.minecraft\forge\accounts.json",
        @"AppData\Roaming\.minecraft\fabric\accounts.json",
        @"AppData\Roaming\VoidLauncher\accounts.json",
        @"AppData\Roaming\.minecraft\legacy\accounts.json",
        @"AppData\Roaming\Salwyrr\accounts.json",
        @"AppData\Roaming\.minecraft\launcher_msa_credentials.json",
        @"AppData\Roaming\.minecraft\TlauncherProfiles.json",
        @"AppData\Roaming\HMCL\accounts.json",
        @"AppData\Roaming\.minecraft\HMCL\accounts.json",
        @"AppData\Roaming\PCL\accounts.json",
        @"AppData\Roaming\Impact\accounts.json",
        @"AppData\Roaming\.minecraft\Impact\accounts.json",
        @"AppData\Roaming\Wurst\accounts.json",
        @"AppData\Roaming\.minecraft\Wurst\accounts.json",
        @"AppData\Roaming\LiquidBounce\accounts.json",
        @"AppData\Roaming\.minecraft\LiquidBounce\accounts.json",
        @"AppData\Roaming\Aristois\accounts.json",
        @"AppData\Roaming\.minecraft\Aristois\accounts.json",
        @"AppData\Roaming\Nova\accounts.json",
        @"AppData\Roaming\Meteor\accounts.json",
        @"AppData\Roaming\.meteor\accounts.json",
        @"AppData\Roaming\Inertia\accounts.json",
        @"AppData\Roaming\Sigma\accounts.json",
        @"AppData\Roaming\.sigma\accounts.json",
        @"AppData\Roaming\Konas\accounts.json",
        @"AppData\Roaming\Future\accounts.json",
        @"AppData\Roaming\Kami\accounts.json",
        @"AppData\Roaming\Salhack\accounts.json",
        @"AppData\Roaming\Rusherhack\accounts.json",
        @"AppData\Roaming\Pyro\accounts.json",
        @"AppData\Roaming\Phobos\accounts.json",
        @"AppData\Roaming\TL Legacy\accounts.json",
        @"AppData\Roaming\Minecraft Launcher\accounts.json",
    };

    private static readonly Regex SettingUser = new(@"Setting user:\s*(\S+)", RegexOptions.Compiled);
    private static readonly Regex ValidMcName = new(@"^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);
    private static readonly Regex ValidDcName = new(@"^[A-Za-z0-9_.]{2,32}$", RegexOptions.Compiled);

    private static string S(string b64) => Encoding.UTF8.GetString(Convert.FromBase64String(b64));

    private static readonly string[] DiscordClientDirs =
    {
        S("QXBwRGF0YVxSb2FtaW5nXGRpc2NvcmQ="),
        S("QXBwRGF0YVxSb2FtaW5nXGRpc2NvcmRjYW5hcnk="),
        S("QXBwRGF0YVxSb2FtaW5nXGRpc2NvcmRwdGI="),
        S("QXBwRGF0YVxSb2FtaW5nXGRpc2NvcmRkZXZlbG9wbWVudA=="),
    };

    private static readonly string[] DiscordStorageSubDirs =
    {
        S("TG9jYWwgU3RvcmFnZVxsZXZlbGRi"),
        S("SW5kZXhlZERC"),
    };

    private static readonly string[] BrowserDataDirs =
    {
        S("QXBwRGF0YVxMb2NhbFxHb29nbGVcQ2hyb21lXFVzZXIgRGF0YQ=="),
        S("QXBwRGF0YVxMb2NhbFxHb29nbGVcQ2hyb21lIEJldGFcVXNlciBEYXRh"),
        S("QXBwRGF0YVxMb2NhbFxHb29nbGVcQ2hyb21lIERldlxVc2VyIERhdGE="),
        S("QXBwRGF0YVxMb2NhbFxHb29nbGVcQ2hyb21lIENhbmFyeVxVc2VyIERhdGE="),
        S("QXBwRGF0YVxMb2NhbFxNaWNyb3NvZnRcRWRnZVxVc2VyIERhdGE="),
        S("QXBwRGF0YVxMb2NhbFxNaWNyb3NvZnRcRWRnZSBCZXRhXFVzZXIgRGF0YQ=="),
        S("QXBwRGF0YVxMb2NhbFxNaWNyb3NvZnRcRWRnZSBEZXZcVXNlciBEYXRh"),
        S("QXBwRGF0YVxMb2NhbFxNaWNyb3NvZnRcRWRnZSBDYW5hcnlcVXNlciBEYXRh"),
        S("QXBwRGF0YVxMb2NhbFxCcmF2ZVNvZnR3YXJlXEJyYXZlLUJyb3dzZXJcVXNlciBEYXRh"),
        S("QXBwRGF0YVxMb2NhbFxCcmF2ZVNvZnR3YXJlXEJyYXZlLUJyb3dzZXItQmV0YVxVc2VyIERhdGE="),
        S("QXBwRGF0YVxMb2NhbFxCcmF2ZVNvZnR3YXJlXEJyYXZlLUJyb3dzZXItTmlnaHRseVxVc2VyIERhdGE="),
        S("QXBwRGF0YVxMb2NhbFxDaHJvbWl1bVxVc2VyIERhdGE="),
        S("QXBwRGF0YVxMb2NhbFxWaXZhbGRpXFVzZXIgRGF0YQ=="),
        S("QXBwRGF0YVxMb2NhbFxPcGVyYSBTb2Z0d2FyZVxPcGVyYSBHWCBTdGFibGU="),
        S("QXBwRGF0YVxMb2NhbFxPcGVyYSBTb2Z0d2FyZVxPcGVyYSBCZXRh"),
        S("QXBwRGF0YVxMb2NhbFxPcGVyYSBTb2Z0d2FyZVxPcGVyYSBEZXZlbG9wZXI="),
        S("QXBwRGF0YVxMb2NhbFxPcGVyYSBTb2Z0d2FyZVxPcGVyYSBOZW9u"),
        S("QXBwRGF0YVxSb2FtaW5nXE9wZXJhIFNvZnR3YXJlXE9wZXJhIFN0YWJsZQ=="),
        S("QXBwRGF0YVxSb2FtaW5nXE9wZXJhIFNvZnR3YXJlXE9wZXJhIEJldGE="),
        S("QXBwRGF0YVxSb2FtaW5nXE9wZXJhIFNvZnR3YXJlXE9wZXJhIERldmVsb3Blcg=="),
        S("QXBwRGF0YVxMb2NhbFxZYW5kZXhcWWFuZGV4QnJvd3NlclxVc2VyIERhdGE="),
        S("QXBwRGF0YVxMb2NhbFxFcGljIFByaXZhY3kgQnJvd3NlclxVc2VyIERhdGE="),
        S("QXBwRGF0YVxMb2NhbFxJcmlkaXVtXFVzZXIgRGF0YQ=="),
        S("QXBwRGF0YVxMb2NhbFxDZW50QnJvd3NlclxVc2VyIERhdGE="),
        S("QXBwRGF0YVxMb2NhbFxDb21vZG9cRHJhZ29uXFVzZXIgRGF0YQ=="),
        S("QXBwRGF0YVxMb2NhbFxTbGltamV0XFVzZXIgRGF0YQ=="),
        S("QXBwRGF0YVxMb2NhbFxJcm9uXFVzZXIgRGF0YQ=="),
        S("QXBwRGF0YVxMb2NhbFxNYXh0aG9uM1xVc2VyIERhdGE="),
        S("QXBwRGF0YVxSb2FtaW5nXDM2MENocm9tZVxDaHJvbWVcVXNlciBEYXRh"),
        S("QXBwRGF0YVxSb2FtaW5nXFRlbmNlbnRcUVFCcm93c2VyXFVzZXIgRGF0YQ=="),
    };

    private static readonly string[] FirefoxProfileDirs =
    {
        S("QXBwRGF0YVxSb2FtaW5nXE1vemlsbGFcRmlyZWZveFxQcm9maWxlcw=="),
        S("QXBwRGF0YVxSb2FtaW5nXFdhdGVyZm94XFByb2ZpbGVz"),
        S("QXBwRGF0YVxSb2FtaW5nXE1vemlsbGFcU2VhTW9ua2V5XFByb2ZpbGVz"),
        S("QXBwRGF0YVxSb2FtaW5nXE1vb25jaGlsZCBQcm9kdWN0aW9uc1xQYWxlIE1vb25cUHJvZmlsZXM="),
        S("QXBwRGF0YVxSb2FtaW5nXE1vb25jaGlsZCBQcm9kdWN0aW9uc1xCYXNpbGlza1xQcm9maWxlcw=="),
    };

    private static readonly string[] FirefoxProfileRoots =
    {
        S("QXBwRGF0YVxSb2FtaW5nXHRvciBicm93c2VyXEJyb3dzZXJcVG9yQnJvd3NlclxEYXRhXEJyb3dzZXJccHJvZmlsZS5kZWZhdWx0"),
    };

    private static readonly string UserDataDirName = S("VXNlciBEYXRh");
    private static readonly string ProfilesDirName = S("UHJvZmlsZXM=");
    private static readonly string AppDataLocalRoot = S("QXBwRGF0YVxMb2NhbA==");
    private static readonly string AppDataRoamingRoot = S("QXBwRGF0YVxSb2FtaW5n");
    private static readonly string LocalStorageDirName = S("TG9jYWwgU3RvcmFnZQ==");
    private static readonly string IndexedDbDirName = S("SW5kZXhlZERC");
    private static readonly string SyncDataDirName = S("U3luYyBEYXRh");
    private static readonly string WebAppsStoreFile = S("d2ViYXBwc3N0b3JlLnNxbGl0ZQ==");

    private static readonly string[] BrowserStorageSubDirs =
    {
        S("TG9jYWwgU3RvcmFnZVxsZXZlbGRi"),
        S("U2Vzc2lvbiBTdG9yYWdl"),
        S("SW5kZXhlZERC"),
        S("U3luYyBEYXRhXExldmVsREI="),
    };

    private static readonly string FirefoxStorageDir = S("c3RvcmFnZQ==");

    private static readonly string SettingsFileName = S("c2V0dGluZ3MuanNvbg==");
    private static readonly string UserIdCacheKey = S("dXNlcl9pZF9jYWNoZQ==");

    private static readonly Regex DiscordUserIdCache = new(
        Regex.Escape(UserIdCacheKey) + @"\D{0,80}?(\d{17,19})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DiscordUserIdName = new(
        "\"id\"\\s*:\\s*\"(\\d{17,19})\"" + "[^}]{0,180}?" + "\"username\"\\s*:\\s*\"([^\"]{2,32})\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DiscordNameUserId = new(
        "\"username\"\\s*:\\s*\"([^\"]{2,32})\"" + "[^}]{0,180}?" + "\"id\"\\s*:\\s*\"(\\d{17,19})\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DiscordWithEmail = new(
        "\"id\"\\s*:\\s*\"(\\d{17,19})\"\\s*,\\s*\"username\"\\s*:\\s*\"[^\"]{2,32}\"\\s*,\\s*\"email\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyDiscordId = new(
        "\"id\"\\s*:\\s*\"(\\d{17,19})\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyUsername = new(
        "\"username\"\\s*:\\s*\"([^\"]{2,32})\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<AltDetectorResult> RunAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Loading cached results...");
        LoadCachedResults(ct);
        if (_cachedMcCount > 0)
            progress?.Report($"Loaded {_cachedMcCount} accounts from cache...");

        progress?.Report("Starting scan...");
        ScanLauncherFiles(ct);
        progress?.Report($"Launchers: {_launcherFiles} files, {_users.Count} accounts");

        ScanDiscordClient(ct);
        progress?.Report($"Discord client: {_discordIds.Count} IDs from {_discordDirs} dirs");

        ScanBrowserData(ct);
        progress?.Report($"Browsers: {_discordIds.Count} IDs from {_browserDirs} storage dirs");

        await ScanLogFilesAsync(ct);
        progress?.Report($"Logs: {_logFiles} files");

        SaveCachedResults();
        progress?.Report($"Scan complete! Found {_users.Count} Minecraft accounts and {_discordIds.Count} Discord IDs");

        List<string> usersSnap;
        List<string> idsSnap;
        List<KeyValuePair<string, string>> acctsSnap;
        lock (_lock)
        {
            usersSnap = _users.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            idsSnap = _discordIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
            acctsSnap = _discordAccounts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        }

        return new AltDetectorResult
        {
            MinecraftAccounts = usersSnap,
            DiscordIds = idsSnap,
            DiscordAccounts = acctsSnap
                .Select(kv => new DiscordAccount { Id = kv.Key, Username = kv.Value, Source = S("ZGlzY29yZGNsaWVudA==") })
                .ToList(),
            LauncherFilesScanned = _launcherFiles,
            LogFilesScanned = _logFiles,
            DiscordDirectoriesScanned = _discordDirs,
            BrowserDirectoriesScanned = _browserDirs,
            CachedMcCount = _cachedMcCount,
            CachedDcCount = _cachedDcCount,
        };
    }

    public void Clear()
    {
        _users.Clear();
        _discordIds.Clear();
        _discordAccounts.Clear();
        _cachedMcCount = _cachedDcCount = 0;
        _launcherFiles = _logFiles = _discordDirs = _browserDirs = 0;
        try { if (File.Exists(CacheFile)) File.Delete(CacheFile); } catch { }
    }

    private void LoadCachedResults(CancellationToken ct)
    {
        if (!File.Exists(CacheFile)) return;
        try
        {
            foreach (var line in File.ReadAllLines(CacheFile))
            {
                ct.ThrowIfCancellationRequested();
                var parts = line.Split('|');
                if (parts.Length != 2) continue;
                var kind = parts[0].Trim();
                var value = parts[1].Trim();
                if (kind == "MC" && !string.IsNullOrWhiteSpace(value) && _users.Add(value))
                    _cachedMcCount++;
                else if (kind == "DC" && !string.IsNullOrWhiteSpace(value) && _discordIds.Add(value))
                    _cachedDcCount++;
            }
        }
        catch { }
    }

    private void SaveCachedResults()
    {
        try
        {
            List<string> usersSnap;
            List<string> idsSnap;
            lock (_lock)
            {
                usersSnap = _users.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                idsSnap = _discordIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
            }
            var sb = new StringBuilder();
            foreach (var u in usersSnap)
                sb.AppendLine("MC|" + u);
            foreach (var d in idsSnap)
                sb.AppendLine("DC|" + d);

            string? dir = Path.GetDirectoryName(CacheFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = CacheFile + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Move(tmp, CacheFile, overwrite: true);
        }
        catch { }
    }

    private static IEnumerable<string> GetFixedUserDirs()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            IEnumerable<string> dirs = Enumerable.Empty<string>();
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                string users = Path.Combine(drive.Name, "Users");
                if (!Directory.Exists(users)) continue;
                dirs = Directory.GetDirectories(users);
            }
            catch { continue; }
            foreach (var userDir in dirs)
            {
                if (IsSystemUserDir(userDir)) continue;
                yield return userDir;
            }
        }
    }

    private void ScanLauncherFiles(CancellationToken ct)
    {
        foreach (var userDir in GetFixedUserDirs())
        {
            ct.ThrowIfCancellationRequested();

            ProcessOverwolfLog(Path.Combine(userDir,
                @"AppData\Roaming\ow-electron\jilehohlakeokncafogkgnicgndeecdiengddbcc\logs\overlay\overlay.log"));
            ProcessOverwolfLog(Path.Combine(userDir,
                @"AppData\Roaming\ow-electron\jilehohlakeokncafogkgnicgndeecdiengddbcc\logs\utility\utility.log"));

            foreach (var rel in LauncherFiles)
            {
                ct.ThrowIfCancellationRequested();
                string p;
                try { p = Path.Combine(userDir, rel); }
                catch { continue; }
                if (File.Exists(p)) ProcessLauncherFile(p);
            }

            foreach (var inst in new[]
            {
                Path.Combine(userDir, @"AppData\Roaming\PrismLauncher\instances"),
                Path.Combine(userDir, @"AppData\Roaming\MultiMC\instances"),
                Path.Combine(userDir, @"AppData\Roaming\PolyMC\instances")
            })
            {
                if (!Directory.Exists(inst)) continue;
                string[] subdirs;
                try { subdirs = Directory.GetDirectories(inst); }
                catch { continue; }
                foreach (var d in subdirs)
                {
                    ct.ThrowIfCancellationRequested();
                    var f = Path.Combine(d, "accounts.json");
                    if (File.Exists(f)) ProcessLauncherFile(f);
                }
            }
        }

        ProcessLauncherFile(@"C:\Program Files (x86)\Minecraft\launcher_profiles.json");
    }

    private const int MaxLauncherFileBytes = 8 * 1024 * 1024;
    private const int MaxLogFileBytes = 8 * 1024 * 1024;
    private const int MaxDecompressedLogBytes = 32 * 1024 * 1024;

    private void ProcessOverwolfLog(string p)
    {
        try
        {
            byte[]? data = ForensicUtil.ReadAllBytesBounded(p, MaxLogFileBytes);
            if (data is null) return;
            foreach (Match m in Regex.Matches(Encoding.UTF8.GetString(data),
                @"--username\s+([a-zA-Z0-9_]{3,16})\b", RegexOptions.IgnoreCase))
            {
                if (m.Groups.Count > 1) TryAddUsername(m.Groups[1].Value);
            }
        }
        catch { }
    }

    private void ProcessLauncherFile(string p)
    {
        try
        {
            byte[]? data = ForensicUtil.ReadAllBytesBounded(p, MaxLauncherFileBytes);
            if (data is null) return;
            string text = Encoding.UTF8.GetString(data);
            if (text.Length == 0) return;
            Interlocked.Increment(ref _launcherFiles);
            try
            {
                ExtractUsernamesFromJson(JsonNode.Parse(text), null);
            }
            catch
            {
                ExtractUsernamesWithRegex(text);
            }
        }
        catch { }
    }

    private void ExtractUsernamesFromJson(JsonNode? token, string? parentKey)
    {
        if (token is JsonObject obj)
        {
            foreach (var prop in obj)
            {
                var name = prop.Key;
                if (name.Equals("username", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("displayname", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("playername", StringComparison.OrdinalIgnoreCase))
                {
                    TryAddUsername(AsString(prop.Value));
                }
                ExtractUsernamesFromJson(prop.Value, name);
            }
        }
        else if (token is JsonArray arr)
        {
            foreach (var item in arr)
                ExtractUsernamesFromJson(item, parentKey);
        }
    }

    private static string? AsString(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        try { return v.GetValue<string>(); }
        catch { }
        if (v.TryGetValue(out long l)) return l.ToString();
        return null;
    }

    private void ExtractUsernamesWithRegex(string content)
    {
        foreach (Match m in Regex.Matches(content,
            "\"(?:username|name|displayName|playerName)\"\\s*:\\s*\"([a-zA-Z0-9_*]{3,16})\"",
            RegexOptions.IgnoreCase))
        {
            if (m.Groups.Count > 1) TryAddUsername(m.Groups[1].Value);
        }
    }

    private void ExtractUsernamesFromServerLog(string content)
    {
        foreach (Match m in SettingUser.Matches(content))
        {
            if (m.Groups.Count > 1) TryAddUsername(m.Groups[1].Value);
        }
    }

    private void TryAddUsername(string? username)
    {
        if (username != null && IsValidUsername(username))
        {
            lock (_lock) _users.Add(username);
        }
    }

    private static bool IsValidUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        if (!ValidMcName.IsMatch(username)) return false;
        if (UsernameBlacklist.Contains(username)) return false;
        if (Regex.IsMatch(username, "^Player\\d+$", RegexOptions.IgnoreCase)) return false;
        return true;
    }

    private static bool IsValidDiscordName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!ValidDcName.IsMatch(name)) return false;
        if (name.StartsWith('.') || name.EndsWith('.')) return false;
        if (name.Contains("..")) return false;
        return true;
    }

    private void ScanDiscordClient(CancellationToken ct)
    {
        foreach (var userDir in GetFixedUserDirs())
        {
            ct.ThrowIfCancellationRequested();
            foreach (var rel in DiscordClientDirs)
            {
                string dir;
                try { dir = Path.Combine(userDir, rel); }
                catch { continue; }
                if (Directory.Exists(dir)) ScanDiscordAppDirectory(dir, ct);
            }
        }
    }

    private static bool IsSystemUserDir(string userDir)
    {
        var n = Path.GetFileName(userDir);
        return n.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
               n.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
               n.Equals("All Users", StringComparison.OrdinalIgnoreCase);
    }

    private void ScanDiscordAppDirectory(string appDir, CancellationToken ct)
    {
        try
        {
            foreach (var sub in DiscordStorageSubDirs)
            {
                var p = Path.Combine(appDir, sub);
                if (Directory.Exists(p))
                {
                    Interlocked.Increment(ref _discordDirs);
                    ScanDirectoryForDiscord(p, ct);
                }
            }
            ScanFileForDiscord(Path.Combine(appDir, SettingsFileName), ct);
        }
        catch { }
    }

    private static bool IsStorageNoiseDir(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        return name.Equals("Cache", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Code Cache", StringComparison.OrdinalIgnoreCase)
            || name.Equals("GPUCache", StringComparison.OrdinalIgnoreCase)
            || name.Equals("GrShaderCache", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ShaderCache", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Crashpad", StringComparison.OrdinalIgnoreCase)
            || name.Equals("BrowserMetrics", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DawnGraphiteCache", StringComparison.OrdinalIgnoreCase)
            || name.Equals("GraphiteDawnCache", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DawnWebGPUCache", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Shared Dictionary", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System Profile", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase);
    }

    private void ScanDirectoryForDiscord(string directory, CancellationToken ct)
    {
        if (!Directory.Exists(directory)) return;
        const int MaxFiles = 3000;
        int seen = 0;
        var stack = new Stack<string>();
        stack.Push(directory);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string current = stack.Pop();
            string[] files;
            string[] dirs;
            try
            {
                files = Directory.GetFiles(current);
                dirs = Directory.GetDirectories(current);
            }
            catch { continue; }
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (++seen > MaxFiles) return;
                ScanFileForDiscord(file, ct);
            }
            foreach (var d in dirs)
            {
                string name;
                try { name = Path.GetFileName(d); }
                catch { continue; }
                if (IsStorageNoiseDir(name)) continue;
                stack.Push(d);
            }
        }
    }

    private void ScanBrowserData(CancellationToken ct)
    {
        foreach (var userDir in GetFixedUserDirs())
        {
            ct.ThrowIfCancellationRequested();

            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in BrowserDataDirs)
            {
                string p;
                try { p = Path.Combine(userDir, rel); }
                catch { continue; }
                if (Directory.Exists(p)) roots.Add(p);
            }
            foreach (var rel in FirefoxProfileDirs)
            {
                string p;
                try { p = Path.Combine(userDir, rel); }
                catch { continue; }
                if (Directory.Exists(p)) roots.Add(p);
            }
            foreach (var rel in FirefoxProfileRoots)
            {
                string p;
                try { p = Path.Combine(userDir, rel); }
                catch { continue; }
                if (Directory.Exists(p)) roots.Add(p);
            }
            foreach (var found in DiscoverBrowserDirs(userDir, ct))
                roots.Add(found);

            foreach (var root in roots)
                ScanBrowserDirTree(root, ct, 0);
        }
    }

    private void ScanBrowserProfile(string profileDir, CancellationToken ct)
    {
        try
        {
            foreach (var sub in BrowserStorageSubDirs)
            {
                var p = Path.Combine(profileDir, sub);
                if (Directory.Exists(p))
                {
                    Interlocked.Increment(ref _browserDirs);
                    ScanDirectoryForDiscord(p, ct);
                }
            }
        }
        catch { }
    }

    private void ScanFirefoxProfile(string profileDir, CancellationToken ct)
    {
        try
        {
            var storage = Path.Combine(profileDir, FirefoxStorageDir);
            if (Directory.Exists(storage))
            {
                Interlocked.Increment(ref _browserDirs);
                ScanDirectoryForDiscord(storage, ct);
            }
            ScanFileForDiscord(Path.Combine(profileDir, WebAppsStoreFile), ct);
        }
        catch { }
    }

    private void ScanBrowserDirTree(string root, CancellationToken ct, int depth)
    {
        if (depth > 3) return;
        try
        {
            ScanBrowserProfile(root, ct);
            foreach (var sub in Directory.GetDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(sub);
                if (IsNonProfileDir(name)) continue;
                if (IsFirefoxProfileDir(sub))
                    ScanFirefoxProfile(sub, ct);
                else if (IsChromiumProfileDir(sub))
                    ScanBrowserProfile(sub, ct);
                else
                    ScanBrowserDirTree(sub, ct, depth + 1);
            }
        }
        catch { }
    }

    private List<string> DiscoverBrowserDirs(string userDir, CancellationToken ct)
    {
        var found = new List<string>();
        int visited = 0;
        const int MaxVisited = 1200;

        void Walk(string root, int depth)
        {
            if (depth > 3 || visited > MaxVisited || !Directory.Exists(root)) return;
            string[] subdirs;
            try { subdirs = Directory.GetDirectories(root); }
            catch { return; }
            foreach (var d in subdirs)
            {
                ct.ThrowIfCancellationRequested();
                visited++;
                if (visited > MaxVisited) return;
                string name;
                try { name = Path.GetFileName(d); }
                catch { continue; }
                if (IsStorageNoiseDir(name)) continue;
                if (name.Equals(UserDataDirName, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(ProfilesDirName, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".default", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".release", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(d);
                    continue;
                }
                Walk(d, depth + 1);
            }
        }

        try { Walk(Path.Combine(userDir, AppDataLocalRoot), 0); } catch { }
        try { Walk(Path.Combine(userDir, AppDataRoamingRoot), 0); } catch { }
        return found;
    }

    private static bool IsFirefoxProfileDir(string dir)
    {
        var name = Path.GetFileName(dir);
        if (name.EndsWith(".default", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".release", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".dev-edition", StringComparison.OrdinalIgnoreCase))
            return true;
        return Directory.Exists(Path.Combine(dir, FirefoxStorageDir));
    }

    private static bool IsChromiumProfileDir(string dir)
    {
        return Directory.Exists(Path.Combine(dir, LocalStorageDirName)) ||
               Directory.Exists(Path.Combine(dir, IndexedDbDirName)) ||
               Directory.Exists(Path.Combine(dir, SyncDataDirName));
    }

    private static bool IsNonProfileDir(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        return name.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("System Profile", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Profile", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Shared Dictionary", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Crashpad", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("BrowserMetrics", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GrShaderCache", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ShaderCache", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GPUCache", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("DawnGraphiteCache", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GraphiteDawnCache", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("DawnWebGPUCache", StringComparison.OrdinalIgnoreCase);
    }

    private void ScanFileForDiscord(string file, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            long len;
            try { len = new FileInfo(file).Length; }
            catch { return; }
            if (len <= 0 || len > 10 * 1024 * 1024) return;
            string ext;
            try { ext = Path.GetExtension(file).ToLowerInvariant(); }
            catch { return; }
            if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".ico"
                or ".ttf" or ".otf" or ".woff" or ".woff2" or ".eot"
                or ".mp4" or ".webm" or ".mp3" or ".wav" or ".ogg" or ".avi" or ".mov"
                or ".dll" or ".exe" or ".node" or ".pak" or ".bin") return;

            byte[]? bytes = ForensicUtil.ReadAllBytesBounded(file, 10 * 1024 * 1024);
            if (bytes is null || bytes.Length == 0) return;
            if (!MayContainDiscordId(bytes)) return;
            ExtractDiscordIdsFromBytes(bytes);
            if (ext is ".log" or ".ldb")
            {
                var records = ext == ".log"
                    ? LevelDbReader.ReadLog(bytes)
                    : LevelDbReader.ReadTable(bytes);
                ExtractDiscordFromRecords(records);
            }
            else if (ext is ".sqlite" or ".sqlite3" or ".db")
            {
                foreach (var row in SqliteReader.ReadRowTexts(bytes))
                    ExtractDiscordIdsDeep(row);
            }
        }
        catch { }
    }

    private static bool MayContainDiscordId(byte[] bytes)
    {
        int digits = 0;
        int scan = Math.Min(bytes.Length, 256 * 1024);
        for (int i = 0; i < scan; i++)
        {
            byte b = bytes[i];
            if (b >= (byte)'0' && b <= (byte)'9') digits++;
            else if (b == (byte)'"' || b == (byte)'i' || b == (byte)'d') continue;
            if (digits >= 17) return true;
        }
        if (scan < bytes.Length)
        {
            for (int i = scan; i < bytes.Length; i += 4096)
            {
                byte b = bytes[i];
                if (b >= (byte)'0' && b <= (byte)'9') { digits += 4; if (digits >= 17) return true; }
            }
        }
        return digits >= 17;
    }

    private void ExtractDiscordFromRecords(List<(string Key, string Value)> records)
    {
        foreach (var (key, value) in records)
        {
            if (string.IsNullOrEmpty(value)) continue;
            var text = NormalizeRecordValue(value);

            ExtractDiscordIdsDeep(key + "\u0000" + text);
            TryPairLevelDbKeyValue(key, text);
        }
    }

    private static string NormalizeRecordValue(string value)
    {
        if (value.Length < 4) return value;
        int zeros = 0;
        foreach (var c in value) if (c == '\0') zeros++;
        if (zeros * 10 < value.Length * 3) return value;
        var sb = new StringBuilder(value.Length);
        int start = value.StartsWith("\x02\x01", StringComparison.Ordinal) ? 2 : 0;
        for (int i = start; i < value.Length; i++)
            if (value[i] != '\0') sb.Append(value[i]);
        return sb.ToString();
    }

    private void TryPairLevelDbKeyValue(string key, string value)
    {
        Match idMatch = Regex.Match(key, @"(\d{17,19})");
        if (!idMatch.Success) return;
        string id = idMatch.Groups[1].Value;
        if (!IsValidDiscordId(id)) return;
        if (Regex.IsMatch(value, "\"id\"\\s*:\\s*\"\\d{17,19}\"")) return;

        Match nameMatch = Regex.Match(value, "\"username\"\\s*:\\s*\"([^\"]{2,32})\"");
        if (!nameMatch.Success) return;
        AddPair(id, nameMatch.Groups[1].Value);
    }

    private void ExtractDiscordIdsFromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return;
        ExtractDiscordIds(Encoding.Latin1.GetString(bytes));
        if (LooksLikeUtf16(bytes))
        {
            int n = bytes.Length >> 1;
            var compact = new byte[n];
            for (int i = 0; i < n; i++) compact[i] = bytes[i << 1];
            ExtractDiscordIds(Encoding.Latin1.GetString(compact));
        }
    }

    private void ExtractDiscordIds(string content)
    {
        if (content.Length < 17) return;

        ExtractDiscordIdsCore(content, content.Length <= 4096);
    }

    private void ExtractDiscordIdsDeep(string content)
    {
        if (content.Length < 17) return;
        ExtractDiscordIdsCore(content, true);
    }

    private void ExtractDiscordIdsCore(string content, bool deep)
    {
        try
        {
            foreach (Match m in DiscordUserIdCache.Matches(content))
                if (m.Groups.Count > 1) AddDiscordId(m.Groups[1].Value);
        }
        catch { }
        try
        {
            foreach (Match m in DiscordWithEmail.Matches(content))
                if (m.Groups.Count > 1) AddDiscordId(m.Groups[1].Value);
        }
        catch { }
        ExtractIdUsernamePairs(content);
        if (deep) ExtractIdUsernamePairsDeep(content);
    }

    private void AddDiscordId(string? id)
    {
        if (string.IsNullOrEmpty(id) || !IsValidDiscordId(id)) return;
        lock (_lock) _discordIds.Add(id);
    }

    private void ExtractIdUsernamePairs(string content)
    {
        if (content.Length < 24) return;
        foreach (Match m in DiscordUserIdName.Matches(content))
            if (m.Groups.Count == 3) AddPair(m.Groups[1].Value, m.Groups[2].Value);
        foreach (Match m in DiscordNameUserId.Matches(content))
            if (m.Groups.Count == 3) AddPair(m.Groups[2].Value, m.Groups[1].Value);
    }

    private void ExtractIdUsernamePairsDeep(string content)
    {
        string? id = null, name = null;
        foreach (Match m in AnyDiscordId.Matches(content))
        {
            if (!IsValidDiscordId(m.Groups[1].Value)) continue;
            if (id != null) return;
            id = m.Groups[1].Value;
        }
        foreach (Match m in AnyUsername.Matches(content))
        {
            if (!IsValidDiscordName(m.Groups[1].Value)) continue;
            if (name != null) return;
            name = m.Groups[1].Value;
        }
        if (id != null && name != null)
            AddPair(id, name);
    }

    private void AddPair(string? id, string? name)
    {
        if (id is null || !IsValidDiscordId(id)) return;
        lock (_lock)
        {
            _discordIds.Add(id);
            if (name is null || !IsValidDiscordName(name)) return;
            if (!_discordAccounts.ContainsKey(id))
                _discordAccounts[id] = name;
        }
    }

    private static bool IsValidDiscordId(string id)
    {
        if (id.Length < 17 || id.Length > 19) return false;
        if (!long.TryParse(id, out var v)) return false;
        long ms = (v >> 22) + 1420070400000L;
        if (ms < 1420070400000L || ms > 1893456000000L) return false;
        if (id.StartsWith("00000") || id.StartsWith("11111") || id.StartsWith("99999")) return false;
        int distinct = 0;
        uint seen = 0;
        foreach (char ch in id)
        {
            uint bit = 1u << (ch - '0');
            if ((seen & bit) == 0) { seen |= bit; if (++distinct >= 3) break; }
        }
        if (distinct < 3) return false;
        if (id.Contains("123456789") || id.Contains("987654321")) return false;
        var date = DateTime.UnixEpoch.AddMilliseconds(ms);
        return date.Year is >= 2015 and <= 2030;
    }

    private static bool LooksLikeUtf16(byte[] bytes)
    {
        if (bytes.Length < 32) return false;
        int printable = 0, wide = 0, total = 0;
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            total++;
            byte lo = bytes[i], hi = bytes[i + 1];
            if (hi == 0 && ((lo >= 0x20 && lo < 0x7f) || lo >= 0x80)) { printable++; wide++; }
            else if ((lo >= 0x20 && lo < 0x7f) || lo >= 0x80) printable++;
        }
        return total >= 32 && printable * 10 > total * 7 && wide * 10 > total * 4;
    }

    private static readonly HashSet<string> LogSkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData",
        "System Volume Information", "$Recycle.Bin", "$RECYCLE.BIN",
        "Cache", "Code Cache", "GPUCache", "Crashpad", "BrowserMetrics",
        "node_modules", ".git", "AppData"
    };

    private const int MaxLogFiles = 4000;

    private async Task ScanLogFilesAsync(CancellationToken ct)
    {
        var files = new List<string>(512);
        foreach (var userDir in GetFixedUserDirs())
        {
            ct.ThrowIfCancellationRequested();
            if (files.Count >= MaxLogFiles) break;
            ScanDirectoryRecursive(userDir, files, ct);
        }

        _logFiles = files.Count;
        await Task.Run(() => Parallel.ForEach(files,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2), CancellationToken = ct },
            ProcessLogFile), ct);
    }

    private void ScanDirectoryRecursive(string directory, List<string> fileList, CancellationToken ct, int depth = 0)
    {
        if (depth > 8 || fileList.Count >= MaxLogFiles)
            return;
        string[] subdirs;
        try
        {
            foreach (var f in Directory.GetFiles(directory, "*.log"))
            {
                ct.ThrowIfCancellationRequested();
                if (fileList.Count >= MaxLogFiles) return;
                try
                {
                    if (new FileInfo(f).Length <= 1024 * 1024) fileList.Add(f);
                }
                catch { }
            }
            foreach (var f in Directory.GetFiles(directory, "*.gz"))
            {
                ct.ThrowIfCancellationRequested();
                if (fileList.Count >= MaxLogFiles) return;
                try
                {
                    if (new FileInfo(f).Length <= 1024 * 1024) fileList.Add(f);
                }
                catch { }
            }
            subdirs = Directory.GetDirectories(directory);
        }
        catch { return; }
        foreach (var d in subdirs)
        {
            string name;
            try { name = Path.GetFileName(d); }
            catch { continue; }
            if (LogSkipDirs.Contains(name)) continue;
            if (name.StartsWith('.')) continue;
            ScanDirectoryRecursive(d, fileList, ct, depth + 1);
            if (fileList.Count >= MaxLogFiles) return;
        }
    }

    private void ProcessLogFile(string filePath)
    {
        try
        {
            long len;
            try { len = new FileInfo(filePath).Length; }
            catch { return; }
            if (len <= 0 || len > 8 * 1024 * 1024) return;
            string content;
            if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 0x4000, FileOptions.SequentialScan);
                    using var gz = new GZipStream(fs, CompressionMode.Decompress);
                    using var ms = new MemoryStream();


                    var chunk = new byte[64 * 1024];
                    int total = 0;
                    int n;
                    while ((n = gz.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        total += n;
                        if (total > MaxDecompressedLogBytes)
                            return;
                        ms.Write(chunk, 0, n);
                    }
                    content = Encoding.UTF8.GetString(ms.ToArray());
                }
                catch { return; }
            }
            else
            {
                byte[]? data = ForensicUtil.ReadAllBytesBounded(filePath, MaxLogFileBytes);
                if (data is null) return;
                content = Encoding.UTF8.GetString(data);
            }
            ExtractUsernamesFromServerLog(content);
        }
        catch { }
    }
}

public sealed class AltDetectorResult
{
    [JsonPropertyName("minecraftAccounts")]
    public List<string> MinecraftAccounts { get; set; } = new();
    [JsonPropertyName("discordIds")]
    public List<string> DiscordIds { get; set; } = new();
    [JsonPropertyName("discordAccounts")]
    public List<DiscordAccount> DiscordAccounts { get; set; } = new();
    [JsonPropertyName("launcherFilesScanned")]
    public int LauncherFilesScanned { get; set; }
    [JsonPropertyName("logFilesScanned")]
    public int LogFilesScanned { get; set; }
    [JsonPropertyName("discordDirectoriesScanned")]
    public int DiscordDirectoriesScanned { get; set; }
    [JsonPropertyName("browserDirectoriesScanned")]
    public int BrowserDirectoriesScanned { get; set; }
    [JsonPropertyName("cachedMcCount")]
    public int CachedMcCount { get; set; }
    [JsonPropertyName("cachedDcCount")]
    public int CachedDcCount { get; set; }
}

public sealed class DiscordAccount
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}
