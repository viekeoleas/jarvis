namespace Jarvis.Speech;

public sealed class PcmAudioFrameEventArgs(short[] samples) : EventArgs
{
    public ReadOnlyMemory<short> Samples { get; } = samples;
}
