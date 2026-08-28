namespace Jarvis.Speech;

public sealed class HandsFreeSessionController : IAsyncDisposable
{
    private readonly IWakeWordListener _wakeListener;
    private readonly IWakeWordEnroller _enroller;
    private readonly LocalSpeechTurnController _speech;
    private readonly Func<CancellationToken, Task> _readyCue;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _lifetime = new();
    private bool _sessionActive;
    private bool _disposed;

    public HandsFreeSessionController(
        IWakeWordListener wakeListener,
        IWakeWordEnroller enroller,
        LocalSpeechTurnController speech,
        Func<CancellationToken, Task> readyCue)
    {
        _wakeListener = wakeListener;
        _enroller = enroller;
        _speech = speech;
        _readyCue = readyCue;
        _wakeListener.Detected += OnWakeDetected;
        _wakeListener.Failed += OnWakeFailed;
        _speech.StateChanged += OnSpeechStateChanged;
        _enroller.ProgressChanged += OnEnrollmentProgress;
    }

    public HandsFreeState State { get; private set; } = HandsFreeState.Disabled;

    public event EventHandler<HandsFreeState>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            await _wakeListener.StartAsync(cancellationToken).ConfigureAwait(false);
            SetState(new HandsFreeState(
                HandsFreePhase.WakeListening,
                "Say Jarvis or press Win+Shift+O"));
        }
        catch (Exception exception)
        {
            SetState(new HandsFreeState(
                HandsFreePhase.Failed,
                "Wake phrase unavailable; hotkey available",
                exception.Message));
        }
    }

    public async Task HandleHotKeyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sessionActive)
        {
            await _speech.ToggleAsync().ConfigureAwait(false);
            return;
        }

        await ActivateAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EnrollAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _sessionActive = false;
            await _wakeListener.StopAsync().ConfigureAwait(false);
            await _speech.CancelAsync().ConfigureAwait(false);
            SetState(new HandsFreeState(HandsFreePhase.Enrolling, "Preparing enrolment"));
            await _enroller.EnrollAsync(cancellationToken).ConfigureAwait(false);
            await _wakeListener.StartAsync(cancellationToken).ConfigureAwait(false);
            SetState(new HandsFreeState(
                HandsFreePhase.WakeListening,
                "Enrolled — say Jarvis"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(HandsFreeState.Disabled);
            throw;
        }
        catch (Exception exception)
        {
            SetState(new HandsFreeState(
                HandsFreePhase.Failed,
                "Enrolment failed; hotkey available",
                exception.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _wakeListener.Detected -= OnWakeDetected;
        _wakeListener.Failed -= OnWakeFailed;
        _speech.StateChanged -= OnSpeechStateChanged;
        _enroller.ProgressChanged -= OnEnrollmentProgress;
        await _wakeListener.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
        _gate.Dispose();
    }

    private async Task ActivateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessionActive)
            {
                return;
            }

            await _wakeListener.StopAsync().ConfigureAwait(false);
            _sessionActive = true;
            SetState(new HandsFreeState(HandsFreePhase.Activating, "Jarvis detected"));
            await _readyCue(cancellationToken).ConfigureAwait(false);
            await _speech.StartAsync().ConfigureAwait(false);
            SetState(new HandsFreeState(
                HandsFreePhase.InSession,
                "Conversation open — listening"));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CloseSessionAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_sessionActive)
            {
                return;
            }

            _sessionActive = false;
            await _wakeListener.StartAsync(_lifetime.Token).ConfigureAwait(false);
            SetState(new HandsFreeState(
                HandsFreePhase.WakeListening,
                "Session closed — say Jarvis"));
        }
        catch (Exception exception)
        {
            SetState(new HandsFreeState(
                HandsFreePhase.Failed,
                "Wake phrase unavailable; hotkey available",
                exception.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StartFollowUpAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_sessionActive)
            {
                return;
            }

            await _speech.StartAsync().ConfigureAwait(false);
            SetState(new HandsFreeState(
                HandsFreePhase.InSession,
                "Follow-up listening — 60 seconds"));
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnWakeDetected(object? sender, WakeWordDetection detection) =>
        _ = ActivateAsync(_lifetime.Token);

    private void OnWakeFailed(object? sender, Exception exception) =>
        SetState(new HandsFreeState(
            HandsFreePhase.Failed,
            "Wake phrase unavailable; hotkey available",
            exception.Message));

    private void OnSpeechStateChanged(object? sender, SpeechTurnState state)
    {
        if (!_sessionActive)
        {
            return;
        }

        if (state.Phase == SpeechTurnPhase.Completed)
        {
            _ = StartFollowUpAsync();
        }
        else if (state.Phase is SpeechTurnPhase.Cancelled or SpeechTurnPhase.Failed)
        {
            _ = CloseSessionAsync();
        }
    }

    private void OnEnrollmentProgress(object? sender, WakeEnrollmentProgress progress) =>
        SetState(new HandsFreeState(HandsFreePhase.Enrolling, progress.Status));

    private void SetState(HandsFreeState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
