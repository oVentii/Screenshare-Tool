using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Serilog;


public sealed class ApiHandler
{
    private static readonly ILogger Logger = Log.ForContext<ApiHandler>();
    private const int MaxMessageBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
    {
        "minimize_window", "close_window",
        "service_checker_run",
        "prefetch_parser_run", "prefetch_clear_cache", "prefetch_usn",
        "prefetch_sysmain", "prefetch_usn_status", "prefetch_export_csv",
        "prefetch_artifacts", "prefetch_export_artifacts", "prefetch_refs",
        "bam_parser_run",
        "alt_detector_run", "alt_detector_clear", "cancel_scan",
        "relaunch_as_admin"
    };

    private readonly CoreWebView2 _webView;
    private readonly Window _window;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _ctsGate = new();
    private CancellationTokenSource? _activeCts;

    
    
    
    private PrefetchScanResult? _lastPrefetchScan;

    public ApiHandler(CoreWebView2 webView, Window window)
    {
        _webView = webView;
        _window = window;
        _dispatcher = window.Dispatcher;
    }

    public async Task HandleMessageAsync(string messageJson)
    {
        if (string.IsNullOrEmpty(messageJson) || Encoding.UTF8.GetByteCount(messageJson) > MaxMessageBytes)
        {
            Logger.Warning("Rejected oversized or empty API message");
            return;
        }

        ApiCallMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ApiCallMessage>(messageJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "Failed to deserialize API message");
            return;
        }

        if (message is null || string.IsNullOrEmpty(message.Id) || string.IsNullOrEmpty(message.Method))
            return;

        if (!AllowedMethods.Contains(message.Method))
        {
            Logger.Warning("Unknown API method: {Method}", message.Method);
            await SendResponseAsync(message.Id, null, "Unknown method");
            return;
        }

        await HandleUiThreadMethodAsync(message);
    }

    private async Task HandleUiThreadMethodAsync(ApiCallMessage message)
    {
        object? result = null;
        string? error = null;

        try
        {
            switch (message.Method)
            {
                case "minimize_window":
                    await _dispatcher.InvokeAsync(() => _window.WindowState = WindowState.Minimized);
                    break;
                case "close_window":
                    await _dispatcher.InvokeAsync(() => _window.Close());
                    break;
                case "service_checker_run":
                    result = await RunExclusiveAsync(ct => ServiceChecker.Run(ct), 150);
                    break;
                case "prefetch_parser_run":
                    result = await RunExclusiveAsync(ct => PrefetchScanner.ScanAsync(ct), 240);
                    _lastPrefetchScan = result as PrefetchScanResult;
                    break;
                case "bam_parser_run":
                    result = await RunExclusiveAsync(ct => BamScanner.ScanAsync(ct), 150);
                    break;
                case "alt_detector_run":
                    result = await RunExclusiveAsync(ct => new AltDetectorScanner().RunAsync(ct: ct), 150);
                    break;
                case "cancel_scan":
                    
                    
                    CancelActiveScan();
                    result = true;
                    break;

                case "alt_detector_clear":
                    result = await RunExclusiveAsync(() =>
                    {
                        new AltDetectorScanner().Clear();
                        return true;
                    });
                    break;
                case "prefetch_clear_cache":
                    SignatureChecker.ClearCache();
                    PrefetchParser.ClearCaches();
                    ForensicUtil.InvalidateLogonCache();
                    _lastPrefetchScan = null;
                    result = true;
                    break;
                case "prefetch_refs":
                    {
                        string pfName = message.Args.Length > 0 &&
                                        message.Args[0].ValueKind == JsonValueKind.String
                            ? message.Args[0].GetString() ?? ""
                            : "";
                        result = FindReferencedFiles(pfName);
                        break;
                    }
                case "prefetch_usn":
                    {
                        char drive = ForensicUtil.GetWindowsDriveLetter();
                        result = await RunExclusiveAsync(() => new UsnJournalReader().RunDetailed(drive));
                        break;
                    }
                case "prefetch_sysmain":
                    result = await Task.Run(PrefetchReport.GetSysMainInfo);
                    break;
                case "prefetch_usn_status":
                    {
                        char drive = ForensicUtil.GetWindowsDriveLetter();
                        result = await Task.Run(() => new UsnJournalReader().CheckIntegrity(drive));
                        break;
                    }
                case "prefetch_export_csv":
                    result = await Task.Run(() => HandleExportCsv(message));
                    break;
                case "prefetch_artifacts":
                    result = await RunExclusiveAsync(LoadArtifacts);
                    break;
                case "prefetch_export_artifacts":
                    result = await Task.Run(ExportArtifacts);
                    break;
                case "relaunch_as_admin":
                    RelaunchAsAdmin();
                    result = true;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            error = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "API method {Method} failed", message.Method);
            error = ex.Message;
        }

        await SendResponseAsync(message.Id, result, error);
    }

    private async Task<object?> RunExclusiveAsync(Func<object?> work)
    {
        await _scanGate.WaitAsync().ConfigureAwait(false);
        try { return await Task.Run(work).ConfigureAwait(false); }
        finally { _scanGate.Release(); }
    }

    
    
    
    
    
    
    private async Task<object?> RunExclusiveAsync(Func<CancellationToken, object?> work, int timeoutSeconds)
        => await RunExclusiveAsync(async ct => await Task.Run(() => work(ct), ct).ConfigureAwait(false), timeoutSeconds)
            .ConfigureAwait(false);

    private async Task<object?> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> work, int timeoutSeconds) where T : class?
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        bool acquired = false;
        lock (_ctsGate) _activeCts = cts;
        try
        {
            
            
            await _scanGate.WaitAsync(cts.Token).ConfigureAwait(false);
            acquired = true;
            return await work(cts.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_ctsGate)
            {
                if (ReferenceEquals(_activeCts, cts)) _activeCts = null;
            }
            if (acquired) _scanGate.Release();
        }
    }

    private void CancelActiveScan()
    {
        lock (_ctsGate)
        {
            try { _activeCts?.Cancel(); } catch {  }
        }
    }

    private void RelaunchAsAdmin()
    {
        string? exe = Environment.ProcessPath ?? Environment.GetCommandLineArgs().FirstOrDefault();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return;
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe)
            };
            if (Process.Start(psi) is not null)
                _dispatcher.InvokeAsync(() => _window.Close());
        }
        catch (Exception ex)
        {
            Logger.Information(ex, "UAC elevation was cancelled or failed");
        }
    }

    private static string HandleExportCsv(ApiCallMessage message)
    {
        try
        {
            if (message.Args.Length == 0)
                return "ERROR: no data to export.";

            string json = message.Args[0].GetRawText();
            if (Encoding.UTF8.GetByteCount(json) > MaxMessageBytes)
                return "ERROR: export payload too large.";

            var entries = JsonSerializer.Deserialize<List<PrefetchInfo>>(json, JsonOptions) ?? new List<PrefetchInfo>();
            if (entries.Count == 0)
                return "ERROR: no entries to export.";
            if (entries.Count > 20_000)
                return "ERROR: too many entries to export.";

            return "Exported to: " + PrefetchReport.ExportCsv(entries);
        }
        catch (Exception ex)
        {
            return "Export failed: " + ex.Message;
        }
    }

    
    
    
    
    
    private object? FindReferencedFiles(string pfFileName)
    {
        var scan = _lastPrefetchScan;
        if (scan is null || string.IsNullOrEmpty(pfFileName))
            return null;

        var entry = scan.Entries
            .Concat(scan.RecoveredDeleted)
            .Concat(scan.GhostEntries)
            .FirstOrDefault(e => string.Equals(e.PfFileName, pfFileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return null;

        return new ReferencedFilesPayload
        {
            PfFileName = entry.PfFileName,
            ReferencedFiles = entry.ReferencedFiles
        };
    }

    private async Task SendResponseAsync(string id, object? result, string? error)
    {
        try
        {
            string responseJson = JsonSerializer.Serialize(
                new ApiResponse(id, result, error), JsonOptions);

            if (result is PrefetchScanResult scan &&
                Encoding.UTF8.GetByteCount(responseJson) > 4 * 1024 * 1024)
            {
                
                string slimJson = JsonSerializer.Serialize(scan, JsonOptions);
                var slim = JsonSerializer.Deserialize<PrefetchScanResult>(slimJson, JsonOptions) ?? scan;
                foreach (var e in slim.Entries) e.ReferencedFiles.Clear();
                foreach (var e in slim.RecoveredDeleted) e.ReferencedFiles.Clear();
                foreach (var e in slim.GhostEntries) e.ReferencedFiles.Clear();
                responseJson = JsonSerializer.Serialize(
                    new ApiResponse(id, slim, error), JsonOptions);
            }

            await _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _webView.PostWebMessageAsString(responseJson);
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to post response for call {Id}", id);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to serialize response for call {Id}", id);
        }
    }

    private static object LoadArtifacts()
    {
        var shimReader = new ShimCacheReader();
        var amReader = new AmcacheReader();
        var shim = shimReader.LoadLive();
        var amcache = amReader.LoadLive();
        return new Dictionary<string, object?>
        {
            ["shimCache"] = shim,
            ["amcache"] = amcache,
            ["shimLoaded"] = true,
            ["amLoaded"] = amReader.LastLoadSucceeded,
            ["isAdmin"] = ForensicUtil.IsAdministrator()
        };
    }

    private static string ExportArtifacts()
    {
        try
        {
            var shim = new ShimCacheReader().LoadLive();
            var amcache = new AmcacheReader().LoadLive();
            var payload = new Dictionary<string, object?>
            {
                ["shimCache"] = shim,
                ["amcache"] = amcache,
                ["exportedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            string dir = AppPaths.Exports;

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string path = Path.Combine(dir, $"iRis_Prefetch_Artifacts_{stamp}_{Guid.NewGuid():N}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8);
            return "Exported to: " + path;
        }
        catch (Exception ex)
        {
            return "Export failed: " + ex.Message;
        }
    }
}

internal sealed class ApiCallMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("args")]
    public JsonElement[] Args { get; set; } = Array.Empty<JsonElement>();
}

internal sealed class ReferencedFilesPayload
{
    [JsonPropertyName("pfFileName")]
    public string? PfFileName { get; set; }

    [JsonPropertyName("referencedFiles")]
    public List<PrefetchFileRef> ReferencedFiles { get; set; } = new();
}

internal sealed class ApiResponse
{
    [JsonPropertyName("id")]
    public string Id { get; }

    [JsonPropertyName("result")]
    public object? Result { get; }

    [JsonPropertyName("error")]
    public string? Error { get; }

    public ApiResponse(string id, object? result, string? error)
    {
        Id = id;
        Result = result;
        Error = error;
    }
}
