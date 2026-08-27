namespace Jarvis.Codex;

public sealed class UnavailableAppServerTransport(Exception exception) : IAppServerTransport
{
    public event EventHandler<string>? MessageReceived
    {
        add { }
        remove { }
    }

    public event EventHandler<AppServerTransportExitedEventArgs>? Exited
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        Task.FromException(exception);

    public Task SendAsync(string message, CancellationToken cancellationToken) =>
        Task.FromException(exception);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
