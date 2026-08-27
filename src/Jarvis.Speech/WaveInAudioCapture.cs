using NAudio.Wave;

namespace Jarvis.Speech;

public sealed class WaveInAudioCapture : IAudioCapture
{
    private readonly WaveIn _waveIn;
    private bool _disposed;

    public WaveInAudioCapture(int deviceNumber = -1)
    {
        _waveIn = new WaveIn
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(
                SpeechAudioFormat.SampleRate,
                SpeechAudioFormat.BitsPerSample,
                SpeechAudioFormat.Channels),
            BufferMilliseconds = 32,
            NumberOfBuffers = 3
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
    }

    public bool IsCapturing { get; private set; }

    public event EventHandler<PcmAudioFrameEventArgs>? AudioAvailable;

    public event EventHandler<Exception>? CaptureFailed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsCapturing)
        {
            return;
        }

        _waveIn.StartRecording();
        IsCapturing = true;
    }

    public void Stop()
    {
        if (!IsCapturing)
        {
            return;
        }

        IsCapturing = false;
        _waveIn.StopRecording();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.RecordingStopped -= OnRecordingStopped;
        _waveIn.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var samples = new short[e.BytesRecorded / sizeof(short)];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
        AudioAvailable?.Invoke(this, new PcmAudioFrameEventArgs(samples));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsCapturing = false;
        if (e.Exception is not null)
        {
            CaptureFailed?.Invoke(this, e.Exception);
        }
    }
}
