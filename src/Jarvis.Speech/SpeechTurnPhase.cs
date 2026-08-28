namespace Jarvis.Speech;

public enum SpeechTurnPhase
{
    Idle,
    Listening,
    Transcribing,
    Thinking,
    Speaking,
    Completed,
    Cancelled,
    Failed
}
