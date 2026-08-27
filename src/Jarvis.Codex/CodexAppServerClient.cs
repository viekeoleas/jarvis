using System.Collections.Concurrent;
using System.Text.Json;

namespace Jarvis.Codex;

public sealed class CodexAppServerClient(IAppServerTransport transport) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private long _nextRequestId;
    private bool _initialized;

    public event EventHandler<AppServerNotification>? NotificationReceived;

    public event EventHandler<AppServerTransportExitedEventArgs>? ConnectionLost;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        transport.MessageReceived += OnMessageReceived;
        transport.Exited += OnTransportExited;
        await transport.StartAsync(cancellationToken).ConfigureAwait(false);

        await RequestAsync(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "jarvis_windows",
                    title = "Jarvis for Windows",
                    version = "0.1.0"
                }
            },
            cancellationToken).ConfigureAwait(false);

        await NotifyAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("A duplicate Codex request id was generated.");
        }

        var message = JsonSerializer.Serialize(new { method, id, @params = parameters });
        try
        {
            await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task NotifyAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Serialize(new { method, @params = parameters });
        return transport.SendAsync(message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        transport.MessageReceived -= OnMessageReceived;
        transport.Exited -= OnTransportExited;
        FailPending(new ObjectDisposedException(nameof(CodexAppServerClient)));
        await transport.DisposeAsync().ConfigureAwait(false);
    }

    private void OnMessageReceived(object? sender, string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.TryGetProperty("id", out var idElement) &&
                idElement.ValueKind == JsonValueKind.Number &&
                idElement.TryGetInt64(out var id) &&
                _pending.TryGetValue(id, out var completion))
            {
                if (root.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var errorMessage)
                        ? errorMessage.GetString()
                        : "Codex app-server returned an unknown protocol error.";
                    completion.TrySetException(new CodexProtocolException(message ?? "Unknown Codex error."));
                    return;
                }

                if (root.TryGetProperty("result", out var result))
                {
                    completion.TrySetResult(result.Clone());
                    return;
                }
            }

            if (root.TryGetProperty("method", out var methodElement))
            {
                var method = methodElement.GetString();
                if (!string.IsNullOrWhiteSpace(method))
                {
                    var parameters = root.TryGetProperty("params", out var paramsElement)
                        ? paramsElement.Clone()
                        : EmptyJsonObject();
                    NotificationReceived?.Invoke(this, new AppServerNotification(method, parameters));
                }
            }
        }
        catch (JsonException exception)
        {
            FailPending(new CodexProtocolException(
                $"Codex app-server emitted invalid JSON: {exception.Message}"));
        }
    }

    private void OnTransportExited(object? sender, AppServerTransportExitedEventArgs e)
    {
        var exception = e.Exception ?? new InvalidOperationException("Codex app-server stopped.");
        FailPending(exception);
        ConnectionLost?.Invoke(this, e);
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }
    }

    private static JsonElement EmptyJsonObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
