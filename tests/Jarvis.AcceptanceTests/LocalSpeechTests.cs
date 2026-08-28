using Jarvis.Speech;
using Jarvis.Core;
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

    [Fact]
    public async Task Voice_persona_sends_only_transcript_and_required_behavior_context()
    {
        var inner = new CapturingResponder("Готово.");
        var responder = new VoicePersonaResponder(inner);

        var response = await responder.RespondAsync(
            "Открой блокнот.",
            TestContext.Current.CancellationToken);

        Assert.Equal("Готово.", response);
        Assert.Contains("Respond in Russian", inner.Request);
        Assert.Contains("concise cinematic personal assistant", inner.Request);
        Assert.Contains("ask one brief clarifying", inner.Request);
        Assert.EndsWith("Открой блокнот.", inner.Request);
    }

    [Theory]
    [InlineData("...", "I didn't catch that")]
    [InlineData("[неразборчиво]", "Не расслышал")]
    [InlineData("[нерозбірливо]", "Не розчув")]
    public async Task Uncertain_transcript_gets_local_clarification_without_Codex(
        string transcript,
        string expected)
    {
        var inner = new CapturingResponder("must not be used");
        var responder = new VoicePersonaResponder(inner);

        var response = await responder.RespondAsync(
            transcript,
            TestContext.Current.CancellationToken);

        Assert.StartsWith(expected, response);
        Assert.Empty(inner.Request);
    }

    private sealed class CapturingResponder(string response) : IAssistantResponder
    {
        public string Request { get; private set; } = string.Empty;

        public Task<string> RespondAsync(string request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(response);
        }
    }
}
