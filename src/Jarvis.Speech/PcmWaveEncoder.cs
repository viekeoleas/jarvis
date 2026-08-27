using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.Speech;

public static class PcmWaveEncoder
{
    private const int HeaderSize = 44;

    public static byte[] Encode(ReadOnlySpan<short> samples)
    {
        var dataLength = checked(samples.Length * sizeof(short));
        var wave = new byte[checked(HeaderSize + dataLength)];
        var span = wave.AsSpan();

        Encoding.ASCII.GetBytes("RIFF", span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataLength);
        Encoding.ASCII.GetBytes("WAVEfmt ", span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], SpeechAudioFormat.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SpeechAudioFormat.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(
            span[28..],
            SpeechAudioFormat.SampleRate * SpeechAudioFormat.Channels * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(
            span[32..],
            SpeechAudioFormat.Channels * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], SpeechAudioFormat.BitsPerSample);
        Encoding.ASCII.GetBytes("data", span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataLength);
        MemoryMarshal.AsBytes(samples).CopyTo(span[HeaderSize..]);
        return wave;
    }
}
