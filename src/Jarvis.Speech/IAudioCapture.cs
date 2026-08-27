namespace Jarvis.Speech;

public interface IAudioCapture : IDisposable
{
    bool IsCapturing { get; }

    event EventHandler<PcmAudioFrameEventArgs>? AudioAvailable;

    event EventHandler<Exception>? CaptureFailed;

    void Start();

    void Stop();
}
