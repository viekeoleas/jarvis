using Jarvis.Speech;
using Jarvis.Core;
using Jarvis.Codex;
using Xunit;

namespace Jarvis.AcceptanceTests;

public sealed class LocalSpeechTurnTests
{
    [Fact]
    public async Task Vad_endpoint_runs_the_complete_local_voice_turn()
    {
        using var capture = new FakeCapture();
        using var estimator = new FakeEstimator([0.9f, .. Enumerable.Repeat(0.01f, 22)]);
        var transcriber = new FakeTranscriber("Привет, Джарвис.");
        var output = new FakeSpeechOutput();
        var responder = new FakeResponder("К вашим услугам.");
        using var controller = new LocalSpeechTurnController(
            capture,
            estimator,
            transcriber,
            output,
            responder,
            TimeSpan.FromSeconds(2));
        var phases = new List<SpeechTurnPhase>();
        var completed = new TaskCompletionSource<SpeechTurnState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StateChanged += (_, state) =>
        {
            lock (phases)
            {
                phases.Add(state.Phase);
            }

            if (state.Phase is SpeechTurnPhase.Completed or SpeechTurnPhase.Failed)
            {
                completed.TrySetResult(state);
            }
        };

        await controller.StartAsync();
        for (var index = 0; index < 23; index++)
        {
            capture.Emit(new short[SpeechAudioFormat.VadFrameSamples]);
        }

        var final = await completed.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(SpeechTurnPhase.Completed, final.Phase);
        Assert.Equal("Привет, Джарвис.", final.Transcript);
        Assert.Equal("К вашим услугам.", output.LastText);
        Assert.Equal("Привет, Джарвис.", responder.LastRequest);
        Assert.Equal(WhisperBackend.Vulkan, final.Backend);
        lock (phases)
        {
            Assert.Contains(SpeechTurnPhase.Listening, phases);
            Assert.Contains(SpeechTurnPhase.Transcribing, phases);
            Assert.Contains(SpeechTurnPhase.Thinking, phases);
            Assert.Contains(SpeechTurnPhase.Speaking, phases);
        }
    }

    [Fact]
    public async Task Session_timeout_on_silence_does_not_call_whisper()
    {
        using var capture = new FakeCapture();
        using var estimator = new FakeEstimator([]);
        var transcriber = new FakeTranscriber("must not be used");
        using var controller = new LocalSpeechTurnController(
            capture,
            estimator,
            transcriber,
            new FakeSpeechOutput(),
            new FakeResponder("must not be used"),
            TimeSpan.FromMilliseconds(30));
        var cancelled = new TaskCompletionSource<SpeechTurnState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StateChanged += (_, state) =>
        {
            if (state.Phase == SpeechTurnPhase.Cancelled)
            {
                cancelled.TrySetResult(state);
            }
        };

        await controller.StartAsync();
        var final = await cancelled.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal("No speech heard", final.Status);
        Assert.Equal(0, transcriber.CallCount);
    }

    [Fact]
    public async Task Recorded_turn_fixture_flows_through_fake_Codex_and_local_speech()
    {
        var transport = CodexSessionTests.CreateScriptedTransport(new
        {
            type = "chatgpt",
            email = "fixture@example.com",
            planType = "plus"
        });
        await using var session = new CodexSession(
            new CodexAppServerClient(transport),
            Path.GetTempPath());
        await session.InitializeAsync(TestContext.Current.CancellationToken);
        using var capture = new FakeCapture();
        using var estimator = new FakeEstimator([0.9f, .. Enumerable.Repeat(0.01f, 22)]);
        var output = new FakeSpeechOutput();
        using var controller = new LocalSpeechTurnController(
            capture,
            estimator,
            new FakeTranscriber("Расскажи короткую шутку."),
            output,
            new VoicePersonaResponder(session),
            TimeSpan.FromSeconds(2));
        var completed = new TaskCompletionSource<SpeechTurnState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StateChanged += (_, state) =>
        {
            if (state.Phase is SpeechTurnPhase.Completed or SpeechTurnPhase.Failed)
            {
                completed.TrySetResult(state);
            }
        };

        await controller.StartAsync();
        for (var index = 0; index < 23; index++)
        {
            capture.Emit(new short[SpeechAudioFormat.VadFrameSamples]);
        }

        var final = await completed.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        var turn = transport.SentMessages.Single(
            message => CodexSessionTests.GetMethod(message) == "turn/start");
        var codexInput = turn.GetProperty("params")
            .GetProperty("input")[0]
            .GetProperty("text")
            .GetString();

        Assert.Equal(SpeechTurnPhase.Completed, final.Phase);
        Assert.Equal("At your service, sir.", final.Response);
        Assert.Equal(final.Response, output.LastText);
        Assert.Contains("Расскажи короткую шутку.", codexInput);
        Assert.Contains("Respond in Russian", codexInput);
        Assert.DoesNotContain("RIFF", codexInput);
    }

    private sealed class FakeCapture : IAudioCapture
    {
        public bool IsCapturing { get; private set; }

        public event EventHandler<PcmAudioFrameEventArgs>? AudioAvailable;

        public event EventHandler<Exception>? CaptureFailed
        {
            add { }
            remove { }
        }

        public void Start() => IsCapturing = true;

        public void Stop() => IsCapturing = false;

        public void Emit(short[] samples) =>
            AudioAvailable?.Invoke(this, new PcmAudioFrameEventArgs(samples));

        public void Dispose() => IsCapturing = false;
    }

    private sealed class FakeEstimator(IEnumerable<float> probabilities) : IVoiceProbabilityEstimator
    {
        private readonly Queue<float> _probabilities = new(probabilities);

        public float Estimate(ReadOnlySpan<short> samples) =>
            _probabilities.TryDequeue(out var probability) ? probability : 0.01f;

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeTranscriber(string text) : ILocalTranscriber
    {
        public int CallCount { get; private set; }

        public Task<LocalTranscription> TranscribeAsync(
            ReadOnlyMemory<short> samples,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new LocalTranscription(
                text,
                WhisperBackend.Vulkan,
                TimeSpan.FromMilliseconds(123)));
        }
    }

    private sealed class FakeSpeechOutput : ILocalSpeechOutput
    {
        public string? LastText { get; private set; }

        public Task<TimeSpan> SpeakAsync(string text, CancellationToken cancellationToken)
        {
            LastText = text;
            return Task.FromResult(TimeSpan.FromMilliseconds(45));
        }
    }

    private sealed class FakeResponder(string response) : IAssistantResponder
    {
        public string? LastRequest { get; private set; }

        public Task<string> RespondAsync(string request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }
}
