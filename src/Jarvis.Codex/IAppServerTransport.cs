namespace Jarvis.Codex;

public interface IAppServerTransport : IAsyncDisposable
{
    event EventHandler<string>? MessageReceived;

    event EventHandler<AppServerTransportExitedEventArgs>? Exited;

    Task StartAsync(CancellationToken cancellationToken);

    Task SendAsync(string message, CancellationToken cancellationToken);
}
