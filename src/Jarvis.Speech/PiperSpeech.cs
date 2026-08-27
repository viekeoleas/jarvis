namespace Jarvis.Speech;

public sealed class PiperSpeech(
    PiperProcessSynthesizer synthesizer,
    LocalAudioPlayer player) : ILocalSpeechOutput
{
    public async Task<TimeSpan> SpeakAsync(string text, CancellationToken cancellationToken)
    {
        var language = LocalSpeechLanguageDetector.Detect(text);
        var speech = await synthesizer.SynthesizeAsync(text, language, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await player.PlayAsync(speech, cancellationToken).ConfigureAwait(false);
            return speech.SynthesisElapsed;
        }
        finally
        {
            Array.Clear(speech.Pcm16);
        }
    }
}
