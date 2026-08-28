namespace Jarvis.Speech;

public interface IWakeWordListener : IAsyncDisposable
{
    bool IsListening { get; }

    event EventHandler<WakeWordDetection>? Detected;

    event EventHandler<Exception>? Failed;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}
