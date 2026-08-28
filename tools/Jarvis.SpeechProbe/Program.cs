using System.Text.Json;
using System.Text.Encodings.Web;
using Jarvis.Codex;
using Jarvis.Speech;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

var fixtures = new[]
{
    new Fixture("ru", "Открой блокнот."),
    new Fixture("uk", "Відкрий блокнот."),
    new Fixture("en", "Open Notepad."),
    new Fixture("mixed", "Джарвис, открой Notepad.")
};
var piper = new PiperProcessSynthesizer(PiperOptions.CreateDefault());
var whisper = new WhisperProcessTranscriber(WhisperProcessOptions.CreateDefault());
var results = new List<ProbeResult>();

if (args.Contains("--benchmark-piper", StringComparer.OrdinalIgnoreCase))
{
    var measurements = new List<object>();
    foreach (var text in new[]
             {
                 "Я готов к работе, сэр.",
                 "Все системы работают нормально."
             })
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var speech = await piper.SynthesizeAsync(
            text,
            SpeechLanguage.Russian,
            CancellationToken.None);
        timer.Stop();
        measurements.Add(new
        {
            Text = text,
            TotalMilliseconds = Math.Round(timer.Elapsed.TotalMilliseconds),
            SynthesisMilliseconds = Math.Round(speech.SynthesisElapsed.TotalMilliseconds),
            AudioSeconds = Math.Round(
                speech.Pcm16.Length / (double)(speech.SampleRate * sizeof(short)),
                2)
        });
        Array.Clear(speech.Pcm16);
    }

    Console.WriteLine(JsonSerializer.Serialize(measurements, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }));
    await piper.DisposeAsync();
    return;
}

if (args.Contains("--live-codex", StringComparer.OrdinalIgnoreCase))
{
    var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    await using var session = new CodexSession(
        new CodexAppServerClient(
            new ProcessAppServerTransport(CodexProcessOptions.CreateDefault())),
        Path.Combine(localData, "Jarvis", "Workspace"));
    await session.InitializeAsync(CancellationToken.None);
    var responder = new VoicePersonaResponder(session);
    var response = await responder.RespondAsync(
        "Представься одним коротким предложением.",
        CancellationToken.None);
    Console.WriteLine(response);
    var spoken = await piper.SynthesizeAsync(
        response,
        LocalSpeechLanguageDetector.Detect(response),
        CancellationToken.None);
    try
    {
        await new LocalAudioPlayer().PlayAsync(spoken, CancellationToken.None);
        Console.WriteLine(
            $"Live Plus response spoken; first audio {spoken.SynthesisElapsed.TotalMilliseconds:0} ms.");
    }
    finally
    {
        Array.Clear(spoken.Pcm16);
    }

    return;
}

if (args.Contains("--play", StringComparer.OrdinalIgnoreCase))
{
    var player = new LocalAudioPlayer();
    var speech = await piper.SynthesizeAsync(
        "Системы готовы, сэр.",
        SpeechLanguage.Russian,
        CancellationToken.None);
    try
    {
        await player.PlayAsync(speech, CancellationToken.None);
        Console.WriteLine($"Playback completed; first audio {speech.SynthesisElapsed.TotalMilliseconds:0} ms.");
    }
    finally
    {
        Array.Clear(speech.Pcm16);
    }

    return;
}

foreach (var fixture in fixtures)
{
    var language = LocalSpeechLanguageDetector.Detect(fixture.Text);
    var speech = await piper.SynthesizeAsync(fixture.Text, language, CancellationToken.None);
    short[] samples = [];
    try
    {
        samples = ResampleToWhisper(speech);
        var transcription = await whisper.TranscribeAsync(samples, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(transcription.Text))
        {
            throw new InvalidOperationException($"Whisper returned no text for {fixture.Name}.");
        }

        results.Add(new ProbeResult(
            fixture.Name,
            fixture.Text,
            transcription.Text,
            language.ToString(),
            transcription.Backend.ToString(),
            Math.Round(transcription.Elapsed.TotalMilliseconds),
            Math.Round(speech.SynthesisElapsed.TotalMilliseconds)));
    }
    finally
    {
        Array.Clear(samples);
        Array.Clear(speech.Pcm16);
    }
}

Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
}));

static short[] ResampleToWhisper(SynthesizedSpeech speech)
{
    using var raw = new RawSourceWaveStream(
        new MemoryStream(speech.Pcm16, writable: false),
        new WaveFormat(speech.SampleRate, 16, 1));
    var source = raw.ToSampleProvider();
    var resampler = new WdlResamplingSampleProvider(source, SpeechAudioFormat.SampleRate);
    var expectedSamples = checked((int)Math.Ceiling(
        speech.Pcm16.Length / (double)sizeof(short) *
        SpeechAudioFormat.SampleRate / speech.SampleRate));
    var output = new List<short>(expectedSamples);
    var floats = new float[SpeechAudioFormat.SampleRate];
    int read;
    while ((read = resampler.Read(floats)) > 0)
    {
        for (var index = 0; index < read; index++)
        {
            output.Add((short)Math.Round(
                Math.Clamp(floats[index], -1f, 1f) * short.MaxValue));
        }
    }

    Array.Clear(floats);
    return [.. output];
}

internal sealed record Fixture(string Name, string Text);

internal sealed record ProbeResult(
    string Fixture,
    string Expected,
    string Transcript,
    string Voice,
    string Backend,
    double TranscriptionMilliseconds,
    double FirstAudioMilliseconds);
