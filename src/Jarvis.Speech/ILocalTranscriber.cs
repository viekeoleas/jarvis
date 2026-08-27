namespace Jarvis.Speech;

public interface ILocalTranscriber
{
    Task<LocalTranscription> TranscribeAsync(
        ReadOnlyMemory<short> samples,
        CancellationToken cancellationToken);
}
