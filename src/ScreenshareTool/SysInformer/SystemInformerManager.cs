using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

public sealed class SystemInformerStatus
{
    [JsonPropertyName("installed")]
    public bool Installed { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("installedTag")]
    public string? InstalledTag { get; set; }

    [JsonPropertyName("lastDownload")]
    public string? LastDownload { get; set; }

    [JsonPropertyName("exePath")]
    public string? ExePath { get; set; }

    [JsonPropertyName("folder")]
    public string? Folder { get; set; }

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("fileSizeLabel")]
    public string? FileSizeLabel { get; set; }

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    [JsonPropertyName("latestVersion")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("latestPublishedAt")]
    public string? LatestPublishedAt { get; set; }

    [JsonPropertyName("lastCheckedAt")]
    public string? LastCheckedAt { get; set; }

    [JsonPropertyName("updateAvailable")]
    public bool UpdateAvailable { get; set; }

    [JsonPropertyName("checkNote")]
    public string? CheckNote { get; set; }

    [JsonPropertyName("osArch")]
    public string? OsArch { get; set; }

    [JsonPropertyName("exeArch")]
    public string? ExeArch { get; set; }

    [JsonPropertyName("archMismatch")]
    public bool ArchMismatch { get; set; }
}

public sealed class SystemInformerUpdate
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("offline")]
    public bool Offline { get; set; }

    [JsonPropertyName("currentVersion")]
    public string? CurrentVersion { get; set; }

    [JsonPropertyName("latestVersion")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("publishedAt")]
    public string? PublishedAt { get; set; }

    [JsonPropertyName("updateAvailable")]
    public bool UpdateAvailable { get; set; }

    [JsonPropertyName("checkedAt")]
    public string? CheckedAt { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class SystemInformerProgress
{
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = "idle";

    [JsonPropertyName("percent")]
    public int Percent { get; set; }

    [JsonPropertyName("downloadedBytes")]
    public long DownloadedBytes { get; set; }

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("exePath")]
    public string? ExePath { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

internal static class SystemInformerManager
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SystemInformerManager));

    private const string GithubLatestApi = "https://api.github.com/repos/winsiderss/si-builds/releases/latest";
    private const string CanaryPage = "https://systeminformer.com/canary";
    private const string MetaFileName = "system-informer-meta.json";

    private static readonly object ProgressGate = new();
    private static SystemInformerProgress _progress = new();
    private static CancellationTokenSource? _downloadCts;
    private static Task? _downloadTask;

    private static readonly HttpClient SharedHttp;

    static SystemInformerManager()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All
        };
        SharedHttp = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        SharedHttp.DefaultRequestHeaders.UserAgent.ParseAdd("iRis-Screenshare-Tool/1.0");
        SharedHttp.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static string BaseFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRis-Screenshare-Tool", "system-informer");

    public static string CurrentArchLabel => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        _ => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
    };


    public static SystemInformerStatus GetStatus()
    {
        string folder = BaseFolder;
        string? exe = FindExe(folder);
        var meta = ReadMeta(folder);
        var status = new SystemInformerStatus
        {
            Installed = exe is not null,
            ExePath = exe,
            Folder = folder,
            LatestVersion = meta?.LatestVersion,
            LatestPublishedAt = meta?.LatestPublishedAt,
            LastCheckedAt = meta?.LastCheckedAt
        };

        if (exe is not null)
        {
            status.Version = ReadVersion(exe, folder);
            status.InstalledTag = meta?.Version;
            status.LastDownload = meta?.DownloadedAt;
            status.OsArch = CurrentArchLabel;
            status.ExeArch = GetPeArchLabel(exe);
            status.ArchMismatch = status.ExeArch is not null && !string.Equals(status.ExeArch, status.OsArch, StringComparison.OrdinalIgnoreCase);
            try
            {
                long size = new FileInfo(exe).Length;
                status.FileSizeBytes = size;
                status.FileSizeLabel = FormatBytes(size);
                status.Verified = !status.ArchMismatch && status.ExeArch is not null && size > 1024 * 100;
            }
            catch
            {
                status.Verified = false;
            }
            status.UpdateAvailable = IsUpdateAvailable(meta?.Version, meta?.LatestVersion) || status.ArchMismatch;
            status.CheckNote = status.ArchMismatch
                ? $"Wrong build for this PC ({status.ExeArch} binary on {status.OsArch} Windows). Press Download to fetch the correct one."
                : meta?.LatestVersion is null
                ? "Press “Check for Updates” to compare with the latest Canary."
                : status.UpdateAvailable
                    ? $"A newer Canary ({meta!.LatestVersion}) is available."
                    : "Installed build matches the latest known Canary.";
        }
        return status;
    }


    public static async Task<SystemInformerUpdate> CheckUpdateAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(40));
        CancellationToken token = linked.Token;

        var result = new SystemInformerUpdate
        {
            CheckedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        };
        try
        {
            var folder = BaseFolder;
            var meta = ReadMeta(folder) ?? new SystemInformerMeta();
            string? exe = FindExe(folder);
            result.CurrentVersion = meta.Version ?? (exe is not null ? ReadVersion(exe, folder) : null);

            (string _, string tag, string? publishedAt, string? _, string? _) = await ResolveLatestReleaseAsync(token).ConfigureAwait(false);
            result.LatestVersion = tag;
            result.PublishedAt = publishedAt;
            result.UpdateAvailable = exe is null || IsUpdateAvailable(meta.Version, tag);
            result.Ok = true;

            meta.LatestVersion = tag;
            meta.LatestPublishedAt = publishedAt;
            meta.LastCheckedAt = result.CheckedAt;
            WriteMeta(folder, meta);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result.Offline = true;
            result.Error = "Update check timed out. Check the connection and retry.";
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "System Informer update check failed");
            result.Offline = ex is HttpRequestException || ex is TaskCanceledException;
            result.Error = result.Offline
                ? "No connection to the release server. Showing the last known state."
                : FriendlyError(ex);
        }
        return result;
    }

    private static bool IsUpdateAvailable(string? installedTag, string? latestTag)
    {
        if (string.IsNullOrWhiteSpace(installedTag) || string.IsNullOrWhiteSpace(latestTag))
            return false;
        return !string.Equals(
            installedTag.Trim(), latestTag.Trim(), StringComparison.OrdinalIgnoreCase);
    }


    public static SystemInformerProgress GetProgress()
    {
        lock (ProgressGate)
        {
            return GetProgressLocked();
        }
    }

    public static SystemInformerProgress StartDownload()
    {
        lock (ProgressGate)
        {
            if (_progress.Active)
                return GetProgressLocked();
            try { _downloadCts?.Cancel(); } catch { }
            try { _downloadCts?.Dispose(); } catch { }
            _downloadCts = new CancellationTokenSource();
            _downloadCts.CancelAfter(TimeSpan.FromMinutes(10));
            _progress = new SystemInformerProgress
            {
                Active = true,
                Done = false,
                Ok = false,
                Stage = "Resolving latest Canary release…",
                Percent = 0
            };
            CancellationToken ct = _downloadCts.Token;
            _downloadTask = Task.Run(() => DownloadCoreAsync(ct), ct);
            _downloadTask.ContinueWith(
                t => Logger.Debug(t.Exception, "System Informer download task faulted"),
                TaskContinuationOptions.OnlyOnFaulted);
            return GetProgressLocked();
        }
    }

    public static void CancelDownload()
    {
        lock (ProgressGate)
        {
            try { _downloadCts?.Cancel(); } catch { }
        }
    }

    private static SystemInformerProgress GetProgressLocked()
    {
        return new SystemInformerProgress
        {
            Active = _progress.Active,
            Done = _progress.Done,
            Ok = _progress.Ok,
            Stage = _progress.Stage,
            Percent = _progress.Percent,
            DownloadedBytes = _progress.DownloadedBytes,
            TotalBytes = _progress.TotalBytes,
            Version = _progress.Version,
            ExePath = _progress.ExePath,
            Error = _progress.Error
        };
    }

    private static void SetProgress(string stage, int percent, long downloaded = -1, long total = -1)
    {
        lock (ProgressGate)
        {
            _progress.Stage = stage;
            _progress.Percent = Math.Clamp(percent, 0, 100);
            if (downloaded >= 0) _progress.DownloadedBytes = downloaded;
            if (total >= 0) _progress.TotalBytes = total;
        }
    }

    private static void FinishProgress(bool ok, string? error, string? version, string? exePath, string stage)
    {
        lock (ProgressGate)
        {
            _progress.Active = false;
            _progress.Done = true;
            _progress.Ok = ok;
            _progress.Error = error;
            _progress.Version = version;
            _progress.ExePath = exePath;
            _progress.Stage = stage;
            _progress.Percent = ok ? 100 : _progress.Percent;
        }
    }

    private static async Task DownloadCoreAsync(CancellationToken ct)
    {
        string? version = null;
        try
        {
            Directory.CreateDirectory(BaseFolder);

            SetProgress("Resolving latest Canary release…", 3);
            (string url, string tag, string? publishedAt, string? expectedSha, string assetName) = await ResolveLatestReleaseAsync(ct).ConfigureAwait(false);
            version = tag;
            lock (ProgressGate) { _progress.Version = version; }

            string zipPath = Path.Combine(BaseFolder, "system-informer-canary-latest.zip");
            if (File.Exists(zipPath))
            {
                try { File.Delete(zipPath); } catch { }
            }

            SetProgress($"Downloading {assetName}…", 5);
            await DownloadFileAsync(url, zipPath, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(expectedSha))
            {
                SetProgress("Verifying download integrity (SHA-256)…", 86);
                await VerifySha256Async(zipPath, expectedSha, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            SetProgress("Extracting portable package…", 88);
            string exe = await Task.Run(() => ExtractPortable(zipPath, BaseFolder, ct), ct).ConfigureAwait(false);

            string? exeArch = GetPeArchLabel(exe);
            if (exeArch is null)
                throw new InvalidDataException($"Extracted file is not a valid Windows executable (no PE header): {exe}");
            if (!string.Equals(exeArch, CurrentArchLabel, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Release asset mismatch: downloaded a {exeArch} build but this PC is {CurrentArchLabel}. Remove the install and retry.");

            ct.ThrowIfCancellationRequested();
            try { File.Delete(zipPath); } catch { }

            string downloadedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            long exeBytes = 0;
            try { exeBytes = new FileInfo(exe).Length; } catch { }
            WriteMeta(BaseFolder, new SystemInformerMeta
            {
                Version = version,
                DownloadedAt = downloadedAt,
                ExePath = exe,
                FileSizeBytes = exeBytes,
                LatestVersion = version,
                LatestPublishedAt = publishedAt,
                LastCheckedAt = downloadedAt
            });

            FinishProgress(true, null, version ?? ReadVersion(exe, BaseFolder), exe,
                $"Ready — {version ?? "latest Canary"} extracted.");
        }
        catch (OperationCanceledException)
        {
            FinishProgress(false, "Download cancelled.", version, null, "Download cancelled.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "System Informer download failed");
            FinishProgress(false, FriendlyError(ex), version, null, "Download failed.");
        }
    }

    private static string FriendlyError(Exception ex)
    {
        string msg = ex.Message;
        if (ex is HttpRequestException || ex is TaskCanceledException)
            return "Network error while contacting the release server. Check the connection and retry. (" + msg + ")";
        if (ex is UnauthorizedAccessException || ex is IOException &&
            (msg.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
             msg.Contains("being used", StringComparison.OrdinalIgnoreCase) ||
             msg.Contains("virus", StringComparison.OrdinalIgnoreCase)))
            return "Write blocked — antivirus may have quarantined the download. Allow the iRis folder in your AV, then retry. (" + msg + ")";
        if (ex is InvalidDataException || msg.Contains("zip", StringComparison.OrdinalIgnoreCase))
            return "Extraction failed — the downloaded archive looks corrupt. Retry the download. (" + msg + ")";
        if (msg.Contains("portable", StringComparison.OrdinalIgnoreCase))
            return msg;
        return "Download failed: " + msg;
    }

    private static async Task<(string Url, string Tag, string? PublishedAt, string? Sha256, string AssetName)> ResolveLatestReleaseAsync(CancellationToken ct)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            CancellationToken token = linkedCts.Token;
            using var resp = await SharedHttp.GetAsync(GithubLatestApi, token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
            string tag = doc.RootElement.TryGetProperty("tag_name", out JsonElement tagEl)
                ? tagEl.GetString() ?? "canary" : "canary";
            string? publishedAt = null;
            if (doc.RootElement.TryGetProperty("published_at", out JsonElement pubEl))
            {
                string? raw = pubEl.GetString();
                if (DateTime.TryParse(raw, out DateTime pub))
                    publishedAt = pub.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            if (doc.RootElement.TryGetProperty("assets", out JsonElement assets))
            {
                string osArch = CurrentArchLabel;
                string? bestUrl = null;
                string? bestName = null;
                string? bestSha = null;
                int bestScore = 0;
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("browser_download_url", out JsonElement urlEl) ||
                        !asset.TryGetProperty("name", out JsonElement nameEl))
                        continue;
                    string name = nameEl.GetString() ?? "";
                    string url = urlEl.GetString() ?? "";
                    string? sha = null;
                    if (asset.TryGetProperty("digest", out JsonElement digestEl))
                    {
                        string? digest = digestEl.GetString();
                        if (!string.IsNullOrWhiteSpace(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                            sha = digest.Substring(7);
                    }
                    int score = ScoreCanaryAsset(name, url, osArch);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestUrl = url;
                        bestName = name;
                        bestSha = sha;
                    }
                }
                if (bestUrl is not null)
                {
                    Logger.Information("Selected Canary asset {Asset} (score {Score}) for {Arch}", bestName, bestScore, osArch);
                    return (bestUrl, tag, publishedAt, bestSha, bestName ?? "canary.zip");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning(ex, "GitHub canary API lookup failed, trying systeminformer.com/canary");
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            string html = await SharedHttp.GetStringAsync(CanaryPage, linkedCts.Token).ConfigureAwait(false);
            string osArch = CurrentArchLabel;
            string? bestUrl = null;
            int bestScore = 0;
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(html, "https?://[^\"'\\s<>]+\\.zip", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                string url = m.Value;
                string fileName;
                try { fileName = Path.GetFileName(new Uri(url).LocalPath); }
                catch { fileName = url; }
                int score = ScoreCanaryAsset(fileName, url, osArch);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestUrl = url;
                }
            }
            if (bestUrl is not null)
                return (bestUrl, "canary", null, null, "canary.zip");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning(ex, "Canary page lookup failed");
        }

        throw new InvalidOperationException(
            "Could not resolve the official portable Canary ZIP (tried GitHub si-builds releases and systeminformer.com/canary).");
    }

    private static int ScoreCanaryAsset(string fileName, string url, string osArch)
    {
        if (!url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return 0;
        string lower = (fileName ?? "").ToLowerInvariant();
        if (lower.Contains("setup") || lower.Contains("installer"))
            return 0;
        if (lower.Contains("pdb") || lower.Contains("symbol") || lower.Contains("debug") ||
            lower.Contains("source") || lower.Contains("src"))
            return 0;

        bool isX64 = osArch == "x64";
        bool isArm = osArch == "arm64";
        bool isX86 = osArch == "x86";

        bool hasArm64 = lower.Contains("arm64") || lower.Contains("aarch64");
        bool hasWin64 = lower.Contains("win64") || lower.Contains("x64") || lower.Contains("64-bit") || lower.Contains("64bit");
        bool hasWin32 = lower.Contains("win32") || lower.Contains("x86") || lower.Contains("32-bit") || lower.Contains("32bit");
        bool isPlainBin = lower.EndsWith("-bin.zip") && !hasArm64 && !hasWin64 && !hasWin32;

        if (isX64)
        {
            if (hasArm64 || hasWin32) return 0;
            if (hasWin64) return 100;
            if (isPlainBin) return 80;
            if (lower.Contains("portable") || lower.Contains("bin")) return 60;
            return 10;
        }
        if (isArm)
        {
            if (hasArm64) return 100;
            if (hasWin64 || hasWin32) return 0;
            if (isPlainBin) return 10;
            if (lower.Contains("portable") || lower.Contains("bin")) return 5;
            return 0;
        }
        if (hasWin32) return 100;
        if (hasWin64 || hasArm64) return 0;
        if (lower.Contains("portable") || lower.Contains("bin")) return 50;
        return 10;
    }

    private static async Task DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        using var resp = await SharedHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        lock (ProgressGate) { _progress.TotalBytes = total ?? 0; }

        await using var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        byte[] buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await net.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            int pct = total is > 0 ? (int)(5 + 80L * read / total.Value) : 5;
            SetProgress($"Downloading… {FormatBytes(read)}" + (total is > 0 ? $" / {FormatBytes(total.Value)}" : ""), pct, read, total ?? 0);
        }
        if (read == 0)
            throw new InvalidDataException("Downloaded file is empty.");
    }

    private static async Task VerifySha256Async(string filePath, string expectedHex, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        byte[] hash;
        using (var sha = SHA256.Create())
        await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            byte[] buffer = new byte[81920];
            int n;
            while ((n = await fs.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                sha.TransformBlock(buffer, 0, n, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            hash = sha.Hash ?? Array.Empty<byte>();
        }
        string actual = Convert.ToHexString(hash);
        if (!string.Equals(actual, expectedHex.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(filePath); } catch { }
            throw new InvalidDataException(
                $"Download integrity check failed (SHA-256 mismatch, expected {expectedHex[..Math.Min(12, expectedHex.Length)]}…). The file was deleted — retry the download.");
        }
    }

    private static string? GetPeArchLabel(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 64) return null;
            using var br = new BinaryReader(fs, System.Text.Encoding.ASCII, leaveOpen: false);
            fs.Seek(0, SeekOrigin.Begin);
            if (br.ReadUInt16() != 0x5A4D) return null;
            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = br.ReadInt32();
            if (peOffset <= 0 || peOffset > fs.Length - 6) return null;
            fs.Seek(peOffset, SeekOrigin.Begin);
            if (br.ReadUInt32() != 0x00004550) return null;
            ushort machine = br.ReadUInt16();
            return machine switch
            {
                0x8664 => "x64",
                0x14C => "x86",
                0xAA64 => "arm64",
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractPortable(string zipPath, string baseFolder, CancellationToken ct)
    {
        string extractDir = Path.Combine(baseFolder, "portable");
        if (Directory.Exists(extractDir))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(extractDir))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(entry)) File.Delete(entry);
                    else Directory.Delete(entry, recursive: true);
                }
                catch { }
            }
        }
        else
        {
            Directory.CreateDirectory(extractDir);
        }

        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("ZIP extraction failed: " + ex.Message, ex);
        }

        string? exe = FindExe(baseFolder);
        if (exe is null)
            throw new FileNotFoundException("SystemInformer.exe was not found inside the portable archive.");
        return exe;
    }


    public static string LaunchExisting()
    {
        string? exe = FindExe(BaseFolder);
        if (exe is null)
            throw new FileNotFoundException("No portable System Informer found. Download the Canary first.");

        string? exeArch = GetPeArchLabel(exe);
        if (exeArch is null)
        {
            throw new InvalidDataException(
                $"Found {exe} but it is not a valid Windows executable (damaged download?). " +
                "Press “Remove”, then “Download & Launch Latest Canary” to fetch a clean copy.");
        }
        if (!string.Equals(exeArch, CurrentArchLabel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Installed build is {exeArch} but this PC is {CurrentArchLabel} — Windows cannot run it. " +
                "Press “Remove”, then “Download & Launch Latest Canary” (the downloader now picks the correct architecture automatically).");
        }

        try
        {
            LaunchExe(exe);
        }
        catch (System.ComponentModel.Win32Exception wx) when (wx.NativeErrorCode == 193)
        {
            throw new InvalidOperationException(
                $"Windows refused to start {exe} (OS error 193: wrong architecture or damaged binary). " +
                "Press “Remove”, then download again.", wx);
        }
        return exe;
    }

    public static string RemoveInstall()
    {
        string folder = BaseFolder;
        int removed = 0;
        try
        {
            if (Directory.Exists(folder))
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    try
                    {
                        if (File.Exists(entry)) File.Delete(entry);
                        else Directory.Delete(entry, recursive: true);
                        removed++;
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "System Informer removal hit an error");
        }
        if (FindExe(folder) is not null)
            throw new IOException("Could not remove all files — close System Informer first, then retry.");
        return $"Removed {removed} item{(removed == 1 ? "" : "s")} from {folder}.";
    }

    public static string OpenFolder()
    {
        string folder = BaseFolder;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"")
        {
            UseShellExecute = true
        });
        return folder;
    }

    private static void LaunchExe(string exe)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? BaseFolder
        };
        Process.Start(psi);
    }


    private static string? FindExe(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return null;
            string direct = Path.Combine(folder, "portable", "SystemInformer.exe");
            if (File.Exists(direct)) return direct;
            string flat = Path.Combine(folder, "SystemInformer.exe");
            if (File.Exists(flat)) return flat;
            string[] hits = Directory.GetFiles(folder, "SystemInformer.exe", SearchOption.AllDirectories);
            Array.Sort(hits, StringComparer.OrdinalIgnoreCase);
            foreach (string h in hits)
            {
                string lower = h.ToLowerInvariant();
                if (lower.Contains("setup") || lower.Contains("installer"))
                    continue;
                return h;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadVersion(string exe, string folder)
    {
        try
        {
            var meta = ReadMeta(folder);
            if (!string.IsNullOrWhiteSpace(meta?.Version))
                return meta!.Version;
        }
        catch { }
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            string? v = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }
        catch
        {
            return null;
        }
    }

    private sealed class SystemInformerMeta
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("downloadedAt")]
        public string? DownloadedAt { get; set; }

        [JsonPropertyName("exePath")]
        public string? ExePath { get; set; }

        [JsonPropertyName("fileSizeBytes")]
        public long FileSizeBytes { get; set; }

        [JsonPropertyName("latestVersion")]
        public string? LatestVersion { get; set; }

        [JsonPropertyName("latestPublishedAt")]
        public string? LatestPublishedAt { get; set; }

        [JsonPropertyName("lastCheckedAt")]
        public string? LastCheckedAt { get; set; }
    }

    private static SystemInformerMeta? ReadMeta(string folder)
    {
        try
        {
            string path = Path.Combine(folder, MetaFileName);
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SystemInformerMeta>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteMeta(string folder, SystemInformerMeta meta)
    {
        try
        {
            string path = Path.Combine(folder, MetaFileName);
            string tmp = path + ".tmp";
            string json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to write System Informer meta file");
        }
    }

    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = 1024 * KB;
        if (bytes >= MB) return $"{bytes / (double)MB:0.0} MB";
        if (bytes >= KB) return $"{bytes / (double)KB:0.0} KB";
        return $"{bytes} B";
    }
}
