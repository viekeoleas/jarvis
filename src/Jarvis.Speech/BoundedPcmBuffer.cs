namespace Jarvis.Speech;

public sealed class BoundedPcmBuffer
{
    private readonly short[] _samples;
    private readonly object _sync = new();
    private int _count;

    public BoundedPcmBuffer(TimeSpan maximumDuration)
    {
        if (maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }

        var capacity = checked((int)Math.Ceiling(
            maximumDuration.TotalSeconds * SpeechAudioFormat.SampleRate));
        _samples = new short[capacity];
    }

    public int Capacity => _samples.Length;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    public bool TryAppend(ReadOnlySpan<short> samples)
    {
        lock (_sync)
        {
            if (samples.Length > _samples.Length - _count)
            {
                return false;
            }

            samples.CopyTo(_samples.AsSpan(_count));
            _count += samples.Length;
            return true;
        }
    }

    public short[] Snapshot()
    {
        lock (_sync)
        {
            return _samples.AsSpan(0, _count).ToArray();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            Array.Clear(_samples, 0, _count);
            _count = 0;
        }
    }
}
