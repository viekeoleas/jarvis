namespace Jarvis.Speech;

public sealed record PiperVoiceDefinition(
    SpeechLanguage Language,
    string Name,
    string ModelPath,
    int SampleRate);
