namespace Jarvis.Speech;

public sealed record SpeechTurnState(
    SpeechTurnPhase Phase,
    string Status,
    string? Transcript = null,
    string? Response = null,
    WhisperBackend? Backend = null,
    TimeSpan? TranscriptionElapsed = null,
    TimeSpan? SynthesisElapsed = null,
    string? Error = null)
{
    public static SpeechTurnState Initial { get; } = new(SpeechTurnPhase.Idle, "Ready");
}
