using System.IO;
using System.Windows;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Serilog;

public sealed class ApiHandler : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<ApiHandler>();
    private const int MaxMessageBytes = 8 * 1024 * 1024;
    private const int MaxIdLength = 128;

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
        "alt_detector_run", "alt_detector_clear", "cancel_scan",
        "prefetch_run", "prefetch_related_signatures",
        "bam_run",
        "system_informer_status", "system_informer_download_start",
        "system_informer_progress", "system_informer_cancel",
        "system_informer_check_update", "system_informer_remove",
        "system_informer_launch", "system_informer_open_folder",
        "relaunch_as_admin"
    };

    private readonly CoreWebView2 _webView;
    private readonly Window _window;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _ctsGate = new();
    private CancellationTokenSource _cancelSource = new();
    private bool _disposed;

    public ApiHandler(CoreWebView2 webView, Window window)
    {
        _webView = webView;
        _window = window;
        _dispatcher = window.Dispatcher;
    }

    public async Task HandleMessageAsync(string messageJson)
    {
        if (string.IsNullOrEmpty(messageJson) ||
            Encoding.UTF8.GetByteCount(messageJson) > MaxMessageBytes)
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

        if (message.Id.Length > MaxIdLength)
        {
            Logger.Warning("Rejected API message with oversized id");
            return;
        }

        if (!AllowedMethods.Contains(message.Method))
        {
            Logger.Warning("Unknown API method: {Method}", message.Method);
            await SendResponseAsync(message.Id, null, "Unknown method");
            return;
        }

        await HandleMethodAsync(message);
    }

    private async Task HandleMethodAsync(ApiCallMessage message)
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
                    result = await RunExclusiveAsync(ct => ServiceChecker.RunAsync(ct), 150);
                    break;
                case "alt_detector_run":
                    result = await RunExclusiveAsync<AltDetectorResult>(ct => new AltDetectorScanner().RunAsync(ct: ct), 150);
                    break;
                case "prefetch_run":
                    result = await RunExclusiveAsync<PrefetchResult>(ct => PrefetchScanner.RunAsync(ct), 280);
                    break;
                case "prefetch_related_signatures":
                    List<string> sigPaths = ParseStringArrayArg(message);
                    result = await RunExclusiveAsync<List<PrefetchRelatedSig>>(
                        ct => Task.Run(() => PrefetchScanner.CheckRelatedSignatures(sigPaths, ct), ct), 120);
                    break;
                case "bam_run":
                    result = await RunExclusiveAsync<BamResult>(ct => BamScanner.RunAsync(ct), 280);
                    break;
                case "system_informer_status":
                    result = await Task.Run(() => (object?)SystemInformerManager.GetStatus()).ConfigureAwait(false);
                    break;
                case "system_informer_download_start":
                    result = await Task.Run(() => (object?)SystemInformerManager.StartDownload()).ConfigureAwait(false);
                    break;
                case "system_informer_progress":
                    result = await Task.Run(() => (object?)SystemInformerManager.GetProgress()).ConfigureAwait(false);
                    break;
                case "system_informer_cancel":
                    await Task.Run(() => SystemInformerManager.CancelDownload()).ConfigureAwait(false);
                    result = true;
                    break;
                case "system_informer_check_update":
                    result = await SystemInformerManager.CheckUpdateAsync().ConfigureAwait(false);
                    break;
                case "system_informer_remove":
                    result = await Task.Run(() => (object?)SystemInformerManager.RemoveInstall()).ConfigureAwait(false);
                    break;
                case "system_informer_launch":
                    result = await Task.Run(() => (object?)SystemInformerManager.LaunchExisting()).ConfigureAwait(false);
                    break;
                case "system_informer_open_folder":
                    result = await Task.Run(() => (object?)SystemInformerManager.OpenFolder()).ConfigureAwait(false);
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
                case "relaunch_as_admin":
                    if (RelaunchAsAdmin())
                        result = true;
                    else
                        error = "Could not relaunch with administrator rights (UAC was cancelled or the executable was not found).";
                    break;
            }
        }
        catch (OperationCanceledException ex)
        {
            error = string.IsNullOrEmpty(ex.Message) ? "Scan cancelled." : ex.Message;
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

    private async Task<object?> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> work, int timeoutSeconds)
        where T : class?
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        bool cancelled = false;
        CancellationTokenSource linked;
        CancellationTokenRegistration reg;
        lock (_ctsGate)
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(_cancelSource.Token, timeoutCts.Token);
            reg = _cancelSource.Token.Register(() => cancelled = true);
        }
        using (linked)
        using (reg)
        {
            bool acquired = false;
            try
            {
                await _scanGate.WaitAsync(linked.Token).ConfigureAwait(false);
                acquired = true;
                return await work(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new OperationCanceledException(cancelled ? "Scan cancelled." : "Scan timed out.");
            }
            finally
            {
                if (acquired) _scanGate.Release();
            }
        }
    }

    private static List<string> ParseStringArrayArg(ApiCallMessage message)
    {
        var list = new List<string>();
        try
        {
            if (message.Args is null || message.Args.Length == 0)
                return list;
            JsonElement first = message.Args[0];
            if (first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty("paths", out JsonElement paths))
                first = paths;
            if (first.ValueKind != JsonValueKind.Array)
                return list;
            foreach (JsonElement el in first.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String)
                    continue;
                string? s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    list.Add(s);
            }
        }
        catch
        {
        }
        return list;
    }

    private void CancelActiveScan()
    {
        CancellationTokenSource previous;
        lock (_ctsGate)
        {
            previous = _cancelSource;
            _cancelSource = new CancellationTokenSource();
        }
        try { previous.Cancel(); } catch { }
        try { previous.Dispose(); } catch { }
    }

    private bool RelaunchAsAdmin()
    {
        string? exe = Environment.ProcessPath ?? Environment.GetCommandLineArgs().FirstOrDefault();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return false;
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
            };
            if (Process.Start(psi) is not null)
            {
                _dispatcher.InvokeAsync(() => _window.Close());
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Information(ex, "UAC elevation was cancelled or failed");
            return false;
        }
    }

    private async Task SendResponseAsync(string id, object? result, string? error)
    {
        try
        {
            string responseJson = JsonSerializer.Serialize(new ApiResponse(id, result, error), JsonOptions);

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancellationTokenSource? source = null;
        lock (_ctsGate)
        {
            source = _cancelSource;
        }
        try { source?.Cancel(); } catch { }
        try { source?.Dispose(); } catch { }
        try { _scanGate.Dispose(); } catch { }
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
