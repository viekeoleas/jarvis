namespace Jarvis.Speech;

public sealed record LocalTranscription(
    string Text,
    WhisperBackend Backend,
    TimeSpan Elapsed);
