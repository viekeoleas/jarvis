namespace Jarvis.Speech;

public sealed record SynthesizedSpeech(
    byte[] Pcm16,
    int SampleRate,
    TimeSpan SynthesisElapsed);
