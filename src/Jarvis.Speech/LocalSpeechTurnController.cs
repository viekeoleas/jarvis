using Jarvis.Core;

namespace Jarvis.Speech;

public sealed class LocalSpeechTurnController : IDisposable
{
    private readonly IAudioCapture _capture;
    private readonly IVoiceProbabilityEstimator _voiceEstimator;
    private readonly ILocalTranscriber _transcriber;
    private readonly IAssistantResponder _responder;
    private readonly ILocalSpeechOutput _speechOutput;
    private readonly TimeSpan _sessionTimeout;
    private readonly BoundedPcmBuffer _audio;
    private readonly UtteranceBoundaryDetector _boundaryDetector = new();
    private readonly short[] _vadFrame = new short[SpeechAudioFormat.VadFrameSamples];
    private readonly object _sync = new();
    private CancellationTokenSource? _turnCancellation;
    private CancellationTokenSource? _timeoutCancellation;
    private int _vadFrameCount;
    private bool _finishing;
    private bool _disposed;

    public LocalSpeechTurnController(
        IAudioCapture capture,
        IVoiceProbabilityEstimator voiceEstimator,
        ILocalTranscriber transcriber,
        ILocalSpeechOutput speechOutput,
        IAssistantResponder responder,
        TimeSpan? sessionTimeout = null)
    {
        _capture = capture;
        _voiceEstimator = voiceEstimator;
        _transcriber = transcriber;
        _speechOutput = speechOutput;
        _responder = responder;
        _sessionTimeout = sessionTimeout ?? TimeSpan.FromSeconds(60);
        _audio = new BoundedPcmBuffer(_sessionTimeout);
        _capture.AudioAvailable += OnAudioAvailable;
        _capture.CaptureFailed += OnCaptureFailed;
    }

    public SpeechTurnState State { get; private set; } = SpeechTurnState.Initial;

    public event EventHandler<SpeechTurnState>? StateChanged;

    public Task ToggleAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SpeechTurnPhase phase;
        lock (_sync)
        {
            phase = State.Phase;
        }

        return phase switch
        {
            SpeechTurnPhase.Listening => FinishAsync(timedOut: false),
            SpeechTurnPhase.Transcribing or SpeechTurnPhase.Thinking or
                SpeechTurnPhase.Speaking => CancelAsync(),
            _ => StartAsync()
        };
    }

    public Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancellationToken timeoutToken;
        SpeechTurnState nextState;
        lock (_sync)
        {
            if (State.Phase is SpeechTurnPhase.Listening or
                SpeechTurnPhase.Transcribing or SpeechTurnPhase.Thinking or
                SpeechTurnPhase.Speaking)
            {
                return Task.CompletedTask;
            }

            _audio.Clear();
            Array.Clear(_vadFrame);
            _vadFrameCount = 0;
            _finishing = false;
            _boundaryDetector.Reset();
            _voiceEstimator.Reset();
            _turnCancellation?.Dispose();
            _timeoutCancellation?.Dispose();
            _turnCancellation = new CancellationTokenSource();
            _timeoutCancellation = new CancellationTokenSource();
            timeoutToken = _timeoutCancellation.Token;
            nextState = new SpeechTurnState(SpeechTurnPhase.Listening, "Listening");
            SetStateLocked(nextState);
        }

        NotifyStateChanged(nextState);
        try
        {
            _capture.Start();
            _ = WatchTimeoutAsync(timeoutToken);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }

        return Task.CompletedTask;
    }

    public Task CancelAsync()
    {
        CancellationTokenSource? turnCancellation;
        SpeechTurnState nextState;
        lock (_sync)
        {
            if (State.Phase is not (SpeechTurnPhase.Listening or
                SpeechTurnPhase.Transcribing or SpeechTurnPhase.Speaking))
            {
                return Task.CompletedTask;
            }

            _finishing = true;
            turnCancellation = _turnCancellation;
            _timeoutCancellation?.Cancel();
            nextState = new SpeechTurnState(SpeechTurnPhase.Cancelled, "Cancelled");
            SetStateLocked(nextState);
        }

        NotifyStateChanged(nextState);
        _capture.Stop();
        turnCancellation?.Cancel();
        _audio.Clear();
        Array.Clear(_vadFrame);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _capture.AudioAvailable -= OnAudioAvailable;
        _capture.CaptureFailed -= OnCaptureFailed;
        _timeoutCancellation?.Cancel();
        _turnCancellation?.Cancel();
        _capture.Dispose();
        _voiceEstimator.Dispose();
        _timeoutCancellation?.Dispose();
        _turnCancellation?.Dispose();
        _audio.Clear();
        Array.Clear(_vadFrame);
    }

    private async Task WatchTimeoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_sessionTimeout, cancellationToken).ConfigureAwait(false);
            await FinishAsync(timedOut: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task FinishAsync(bool timedOut)
    {
        short[] samples;
        CancellationToken turnToken;
        SpeechTurnState nextState;
        lock (_sync)
        {
            if (State.Phase != SpeechTurnPhase.Listening || _finishing)
            {
                return;
            }

            _finishing = true;
            _timeoutCancellation?.Cancel();
            turnToken = _turnCancellation?.Token ?? CancellationToken.None;
            samples = _audio.Snapshot();
            _audio.Clear();

            if (!_boundaryDetector.HasSpeech && timedOut)
            {
                nextState = new SpeechTurnState(
                    SpeechTurnPhase.Cancelled,
                    "No speech heard");
            }
            else if (samples.Length < SpeechAudioFormat.VadFrameSamples)
            {
                nextState = new SpeechTurnState(
                    SpeechTurnPhase.Cancelled,
                    "No speech heard");
            }
            else
            {
                nextState = new SpeechTurnState(
                    SpeechTurnPhase.Transcribing,
                    "Transcribing");
            }

            SetStateLocked(nextState);
        }

        NotifyStateChanged(nextState);
        _capture.Stop();
        if (nextState.Phase != SpeechTurnPhase.Transcribing)
        {
            Array.Clear(samples);
            return;
        }

        try
        {
            var transcription = await _transcriber.TranscribeAsync(samples, turnToken)
                .ConfigureAwait(false);
            turnToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(transcription.Text))
            {
                SetState(new SpeechTurnState(
                    SpeechTurnPhase.Cancelled,
                    "No speech recognized",
                    Backend: transcription.Backend,
                    TranscriptionElapsed: transcription.Elapsed));
                return;
            }

            var transcript = transcription.Text.Trim();
            SetState(new SpeechTurnState(
                SpeechTurnPhase.Thinking,
                "Thinking",
                transcript,
                Backend: transcription.Backend,
                TranscriptionElapsed: transcription.Elapsed));
            var response = await _responder.RespondAsync(transcript, turnToken)
                .ConfigureAwait(false);
            turnToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(response))
            {
                throw new InvalidOperationException("Codex returned an empty response.");
            }

            response = response.Trim();
            SetState(new SpeechTurnState(
                SpeechTurnPhase.Speaking,
                "Speaking",
                transcript,
                response,
                transcription.Backend,
                transcription.Elapsed));
            var synthesisElapsed = await _speechOutput.SpeakAsync(response, turnToken)
                .ConfigureAwait(false);
            SetState(new SpeechTurnState(
                SpeechTurnPhase.Completed,
                "Completed",
                transcript,
                response,
                transcription.Backend,
                transcription.Elapsed,
                synthesisElapsed));
        }
        catch (OperationCanceledException) when (turnToken.IsCancellationRequested)
        {
            SetState(new SpeechTurnState(SpeechTurnPhase.Cancelled, "Cancelled"));
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
        finally
        {
            Array.Clear(samples);
        }
    }

    private void OnAudioAvailable(object? sender, PcmAudioFrameEventArgs eventArgs)
    {
        var shouldFinish = false;
        Exception? failure = null;
        try
        {
            lock (_sync)
            {
                if (State.Phase != SpeechTurnPhase.Listening || _finishing)
                {
                    return;
                }

                var samples = eventArgs.Samples.Span;
                if (!_audio.TryAppend(samples))
                {
                    failure = new InvalidOperationException("The bounded microphone buffer is full.");
                }
                else
                {
                    while (!samples.IsEmpty)
                    {
                        var copyCount = Math.Min(_vadFrame.Length - _vadFrameCount, samples.Length);
                        samples[..copyCount].CopyTo(_vadFrame.AsSpan(_vadFrameCount));
                        _vadFrameCount += copyCount;
                        samples = samples[copyCount..];
                        if (_vadFrameCount != _vadFrame.Length)
                        {
                            continue;
                        }

                        var probability = _voiceEstimator.Estimate(_vadFrame);
                        _vadFrameCount = 0;
                        if (_boundaryDetector.Accept(probability, _vadFrame.Length) ==
                            VoiceBoundaryEvent.UtteranceEnded)
                        {
                            shouldFinish = true;
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is not null)
        {
            Fail(failure);
        }
        else if (shouldFinish)
        {
            _ = Task.Run(() => FinishAsync(timedOut: false));
        }
    }

    private void OnCaptureFailed(object? sender, Exception exception) => Fail(exception);

    private void Fail(Exception exception)
    {
        SpeechTurnState nextState;
        lock (_sync)
        {
            _finishing = true;
            _timeoutCancellation?.Cancel();
            _turnCancellation?.Cancel();
            nextState = new SpeechTurnState(
                SpeechTurnPhase.Failed,
                "Speech failed",
                Error: exception.Message);
            SetStateLocked(nextState);
        }

        NotifyStateChanged(nextState);
        _capture.Stop();
        _audio.Clear();
        Array.Clear(_vadFrame);
    }

    private void SetState(SpeechTurnState state)
    {
        lock (_sync)
        {
            SetStateLocked(state);
        }

        NotifyStateChanged(state);
    }

    private void SetStateLocked(SpeechTurnState state)
    {
        State = state;
    }

    private void NotifyStateChanged(SpeechTurnState state) =>
        StateChanged?.Invoke(this, state);
}
