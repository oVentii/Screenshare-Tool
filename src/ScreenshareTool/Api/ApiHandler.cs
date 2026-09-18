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
        "relaunch_as_admin"
    };

    private readonly CoreWebView2 _webView;
    private readonly Window _window;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _ctsGate = new();
    private CancellationTokenSource? _activeCts;
    private volatile bool _cancelRequested;
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
                    RelaunchAsAdmin();
                    result = true;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            error = _cancelRequested ? "Scan cancelled." : "Scan timed out.";
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        bool acquired = false;
        lock (_ctsGate) { _activeCts = cts; _cancelRequested = false; }
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
        lock (_ctsGate)
        {
            _cancelRequested = true;
            try { _activeCts?.Cancel(); } catch { }
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
        lock (_ctsGate)
        {
            try { _activeCts?.Cancel(); } catch { }
            try { _activeCts?.Dispose(); } catch { }
            _activeCts = null;
        }
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
