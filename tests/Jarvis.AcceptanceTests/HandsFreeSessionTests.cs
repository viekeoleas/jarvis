using Jarvis.Core;
using Jarvis.Speech;
using Xunit;

namespace Jarvis.AcceptanceTests;

public sealed class HandsFreeSessionTests
{
    [Fact]
    public async Task Wake_detection_opens_turn_then_follow_up_without_repeating_wake_phrase()
    {
        var wake = new FakeWakeWordListener();
        using var capture = new FakeCapture();
        using var estimator = new FakeEstimator([0.9f, .. Enumerable.Repeat(0.01f, 22)]);
        using var speech = new LocalSpeechTurnController(
            capture,
            estimator,
            new FakeTranscriber(),
            new FakeSpeechOutput(),
            new FakeResponder(),
            TimeSpan.FromMilliseconds(800));
        var cues = 0;
        await using var handsFree = new HandsFreeSessionController(
            wake,
            new FakeEnroller(),
            speech,
            _ =>
            {
                cues++;
                return Task.CompletedTask;
            });
        var observed = new List<string>();
        speech.StateChanged += (_, state) =>
            observed.Add($"speech:{state.Phase}:{state.Error}");
        handsFree.StateChanged += (_, state) => observed.Add($"hands:{state.Phase}");
        await handsFree.StartAsync(TestContext.Current.CancellationToken);

        wake.EmitDetection();
        await WaitUntilAsync(
            () => capture.StartCount == 1,
            TestContext.Current.CancellationToken);
        for (var index = 0; index < 23; index++)
        {
            capture.Emit(new short[SpeechAudioFormat.VadFrameSamples]);
        }

        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.True(
            capture.StartCount >= 2,
            $"Expected follow-up capture. Observed: {string.Join(", ", observed)}");

        Assert.Equal(1, cues);
        Assert.Equal(1, wake.DetectionCount);
        Assert.Equal(HandsFreePhase.InSession, handsFree.State.Phase);

        await WaitUntilAsync(
            () => wake.StartCount == 2,
            TestContext.Current.CancellationToken);
        Assert.Equal(HandsFreePhase.WakeListening, handsFree.State.Phase);
        Assert.Contains("Session closed", handsFree.State.Status);
    }

    [Fact]
    public async Task Hotkey_activates_speech_when_wake_model_is_unavailable()
    {
        var wake = new FakeWakeWordListener { StartFailure = new FileNotFoundException("No model") };
        using var capture = new FakeCapture();
        using var estimator = new FakeEstimator([]);
        using var speech = new LocalSpeechTurnController(
            capture,
            estimator,
            new FakeTranscriber(),
            new FakeSpeechOutput(),
            new FakeResponder(),
            TimeSpan.FromSeconds(1));
        await using var handsFree = new HandsFreeSessionController(
            wake,
            new FakeEnroller(),
            speech,
            _ => Task.CompletedTask);

        await handsFree.StartAsync(TestContext.Current.CancellationToken);
        await handsFree.HandleHotKeyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HandsFreePhase.InSession, handsFree.State.Phase);
        Assert.True(capture.IsCapturing);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeWakeWordListener : IWakeWordListener
    {
        public bool IsListening { get; private set; }

        public int StartCount { get; private set; }

        public int DetectionCount { get; private set; }

        public Exception? StartFailure { get; init; }

        public event EventHandler<WakeWordDetection>? Detected;

        public event EventHandler<Exception>? Failed
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (StartFailure is not null)
            {
                throw StartFailure;
            }

            StartCount++;
            IsListening = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsListening = false;
            return Task.CompletedTask;
        }

        public void EmitDetection()
        {
            DetectionCount++;
            Detected?.Invoke(this, new WakeWordDetection(DateTimeOffset.UtcNow));
        }

        public ValueTask DisposeAsync()
        {
            IsListening = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeEnroller : IWakeWordEnroller
    {
        public event EventHandler<WakeEnrollmentProgress>? ProgressChanged
        {
            add { }
            remove { }
        }

        public Task<string> EnrollAsync(CancellationToken cancellationToken) =>
            Task.FromResult("jarvis.rpw");
    }

    private sealed class FakeCapture : IAudioCapture
    {
        public bool IsCapturing { get; private set; }

        public int StartCount { get; private set; }

        public event EventHandler<PcmAudioFrameEventArgs>? AudioAvailable;

        public event EventHandler<Exception>? CaptureFailed
        {
            add { }
            remove { }
        }

        public void Start()
        {
            StartCount++;
            IsCapturing = true;
        }

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

    private sealed class FakeTranscriber : ILocalTranscriber
    {
        public Task<LocalTranscription> TranscribeAsync(
            ReadOnlyMemory<short> samples,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LocalTranscription(
                "Hello Jarvis",
                WhisperBackend.Vulkan,
                TimeSpan.FromMilliseconds(10)));
    }

    private sealed class FakeSpeechOutput : ILocalSpeechOutput
    {
        public Task<TimeSpan> SpeakAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult(TimeSpan.FromMilliseconds(10));
    }

    private sealed class FakeResponder : IAssistantResponder
    {
        public Task<string> RespondAsync(string request, CancellationToken cancellationToken) =>
            Task.FromResult("At your service.");
    }
}
