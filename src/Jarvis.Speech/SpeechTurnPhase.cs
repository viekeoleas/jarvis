namespace Jarvis.Speech;

public enum SpeechTurnPhase
{
    Idle,
    Listening,
    Transcribing,
    Speaking,
    Completed,
    Cancelled,
    Failed
}
