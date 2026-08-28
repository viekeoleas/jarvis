namespace Jarvis.Speech;

public interface IWakeWordEnroller
{
    event EventHandler<WakeEnrollmentProgress>? ProgressChanged;

    Task<string> EnrollAsync(CancellationToken cancellationToken);
}
