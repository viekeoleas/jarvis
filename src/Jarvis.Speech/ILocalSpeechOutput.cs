namespace Jarvis.Speech;

public interface ILocalSpeechOutput
{
    Task<TimeSpan> SpeakAsync(string text, CancellationToken cancellationToken);
}
