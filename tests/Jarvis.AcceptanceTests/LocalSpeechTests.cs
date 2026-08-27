using Jarvis.Speech;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Jarvis.AcceptanceTests;

public sealed class LocalSpeechTests
{
    [Fact]
    public void Audio_buffer_is_preallocated_bounded_and_never_requires_a_file()
    {
        var buffer = new BoundedPcmBuffer(TimeSpan.FromSeconds(1));
        var fullSecond = new short[SpeechAudioFormat.SampleRate];

        Assert.True(buffer.TryAppend(fullSecond));
        Assert.False(buffer.TryAppend([1]));
        Assert.Equal(SpeechAudioFormat.SampleRate, buffer.Count);
        Assert.Equal(fullSecond, buffer.Snapshot());

        buffer.Clear();
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Long_session_silence_does_not_manufacture_an_utterance_boundary()
    {
        var detector = new UtteranceBoundaryDetector(endSilenceMilliseconds: 700);
        var sixtySecondsOfFrames =
            60 * SpeechAudioFormat.SampleRate / SpeechAudioFormat.VadFrameSamples;

        for (var index = 0; index < sixtySecondsOfFrames; index++)
        {
            Assert.Equal(
                VoiceBoundaryEvent.None,
                detector.Accept(0.01f, SpeechAudioFormat.VadFrameSamples));
        }

        Assert.False(detector.HasSpeech);
    }

    [Fact]
    public void Silero_probabilities_start_and_end_one_utterance_after_bounded_silence()
    {
        var detector = new UtteranceBoundaryDetector(endSilenceMilliseconds: 700);

        Assert.Equal(
            VoiceBoundaryEvent.SpeechStarted,
            detector.Accept(0.9f, SpeechAudioFormat.VadFrameSamples));

        VoiceBoundaryEvent boundary = VoiceBoundaryEvent.None;
        while (boundary == VoiceBoundaryEvent.None)
        {
            boundary = detector.Accept(0.01f, SpeechAudioFormat.VadFrameSamples);
        }

        Assert.Equal(VoiceBoundaryEvent.UtteranceEnded, boundary);
        Assert.False(detector.HasSpeech);
    }

    [Fact]
    public void Whisper_input_is_a_valid_in_memory_pcm_wave_stream()
    {
        short[] samples = [0x1234, -2];

        var wave = PcmWaveEncoder.Encode(samples);

        Assert.Equal(48, wave.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(SpeechAudioFormat.SampleRate, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(24)));
        Assert.Equal(samples[0], BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44)));
        Assert.Equal(samples[1], BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(46)));
    }

    [Theory]
    [InlineData("Привет, сэр.", SpeechLanguage.Russian)]
    [InlineData("Привіт, сер. Усе працює.", SpeechLanguage.Ukrainian)]
    [InlineData("All systems are ready.", SpeechLanguage.English)]
    [InlineData("Jarvis: усі ready.", SpeechLanguage.Ukrainian)]
    public void Local_voice_is_selected_from_response_text(
        string text,
        SpeechLanguage expected)
    {
        Assert.Equal(expected, LocalSpeechLanguageDetector.Detect(text));
    }
}
