using NAudio.Wave;

namespace Jarvis.Speech;

public sealed class LocalAudioPlayer
{
    public async Task PlayAsync(SynthesizedSpeech speech, CancellationToken cancellationToken)
    {
        if (speech.Pcm16.Length == 0)
        {
            return;
        }

        using var source = new RawSourceWaveStream(
            new MemoryStream(speech.Pcm16, writable: false),
            new WaveFormat(speech.SampleRate, bits: 16, channels: 1));
        using var output = new WaveOut();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, eventArgs) =>
        {
            if (eventArgs.Exception is not null)
            {
                completion.TrySetException(eventArgs.Exception);
            }
            else
            {
                completion.TrySetResult();
            }
        };
        using var cancellationRegistration = cancellationToken.Register(output.Stop);

        output.Init(source);
        output.Play();
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
