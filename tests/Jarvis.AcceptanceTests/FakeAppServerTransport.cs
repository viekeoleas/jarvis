using System.Text.Json;
using Jarvis.Codex;

namespace Jarvis.AcceptanceTests;

internal sealed class FakeAppServerTransport : IAppServerTransport
{
    public event EventHandler<string>? MessageReceived;

    public event EventHandler<AppServerTransportExitedEventArgs>? Exited;

    public Func<JsonElement, Task>? MessageHandler { get; set; }

    public List<JsonElement> SentMessages { get; } = [];

    public bool Started { get; private set; }

    public bool CompleteTurn { get; set; } = true;

    public bool Disposed { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Started = true;
        return Task.CompletedTask;
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(message);
        var clone = document.RootElement.Clone();
        SentMessages.Add(clone);
        if (MessageHandler is not null)
        {
            await MessageHandler(clone);
        }
    }

    public void Deliver(object message) =>
        MessageReceived?.Invoke(this, JsonSerializer.Serialize(message));

    public void Exit(Exception? exception = null) =>
        Exited?.Invoke(this, new AppServerTransportExitedEventArgs(exception));

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
