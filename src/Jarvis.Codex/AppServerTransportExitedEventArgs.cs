namespace Jarvis.Codex;

public sealed class AppServerTransportExitedEventArgs(Exception? exception) : EventArgs
{
    public Exception? Exception { get; } = exception;
}
