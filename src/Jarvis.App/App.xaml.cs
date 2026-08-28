using System.IO;
using System.Media;
using System.Threading;
using System.Windows;
using Jarvis.Codex;
using Jarvis.Core;
using Jarvis.Speech;
using Jarvis.Storage;

namespace Jarvis.App;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\Viekeoleas.Jarvis.Mvp";

    private Mutex? _singleInstanceMutex;
    private MainWindow? _window;
    private TrayHost? _trayHost;
    private GlobalHotKey? _hotKey;
    private PanelController? _panelController;
    private CodexSession? _codexSession;
    private LocalSpeechTurnController? _speechController;
    private ConversationHistoryStore? _history;
    private HandsFreeSessionController? _handsFree;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _codexSession = new CodexSession(
            CreateCodexClient,
            Path.Combine(localData, "Jarvis", "Workspace"));

        var historyUnavailableReason = TryCreateHistory();
        IAssistantResponder textResponder = _codexSession;
        IAssistantResponder voiceResponder = new VoicePersonaResponder(_codexSession);
        if (_history is not null)
        {
            textResponder = new HistoryRecordingResponder(
                textResponder,
                _history,
                () => _codexSession.CurrentThreadId);
            voiceResponder = new HistoryRecordingResponder(
                voiceResponder,
                _history,
                () => _codexSession.CurrentThreadId);
        }

        var speechUnavailableReason = TryCreateSpeechController(voiceResponder);
        if (_speechController is not null)
        {
            var wakeOptions = WakeWordOptions.CreateDefault();
            _handsFree = new HandsFreeSessionController(
                new RustpotterWakeWordListener(wakeOptions),
                new RustpotterWakeWordEnroller(wakeOptions),
                _speechController,
                PlayReadyCueAsync);
        }

        var controller = new JarvisController(textResponder);
        _window = new MainWindow(
            controller,
            _codexSession,
            _speechController,
            speechUnavailableReason,
            _history,
            historyUnavailableReason,
            _handsFree);
        _panelController = new PanelController(_window);
        _trayHost = new TrayHost(_window, _panelController.TogglePanel, ExitApplication);
        _hotKey = new GlobalHotKey(_window, OnVoiceHotKeyPressed);
        _ = InitializeCodexAsync();
        if (_handsFree is not null)
        {
            _ = _handsFree.StartAsync(CancellationToken.None);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKey?.Dispose();
        _trayHost?.Dispose();
        _handsFree?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _speechController?.Dispose();
        _history?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _codexSession?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private async Task InitializeCodexAsync()
    {
        try
        {
            if (_codexSession is not null)
            {
                await _codexSession.InitializeAsync(CancellationToken.None);
            }
        }
        catch
        {
            // CodexSession publishes the safe user-facing failure state.
        }

        try
        {
            if (_history is not null && _codexSession is not null)
            {
                var cleanup = new RetentionCleanupService(_history, _codexSession);
                await cleanup.RunAsync(CancellationToken.None);
            }
        }
        catch
        {
            // Expired text is pruned before remote thread deletion and retry tombstones persist.
        }
    }

    private void ExitApplication()
    {
        _window?.AllowClose();
        Shutdown();
    }

    private string? TryCreateSpeechController(IAssistantResponder responder)
    {
        WaveInAudioCapture? capture = null;
        SileroVoiceProbabilityEstimator? estimator = null;
        try
        {
            capture = new WaveInAudioCapture();
            estimator = new SileroVoiceProbabilityEstimator(
                SpeechModelCatalog.GetSileroModelPath());
            var transcriber = new WhisperProcessTranscriber(
                WhisperProcessOptions.CreateDefault());
            var output = new PiperSpeech(
                new PiperProcessSynthesizer(PiperOptions.CreateDefault()),
                new LocalAudioPlayer());
            _speechController = new LocalSpeechTurnController(
                capture,
                estimator,
                transcriber,
                output,
                responder);
            capture = null;
            estimator = null;
            return null;
        }
        catch (Exception exception)
        {
            capture?.Dispose();
            estimator?.Dispose();
            _speechController?.Dispose();
            _speechController = null;
            return exception.Message;
        }
    }

    private string? TryCreateHistory()
    {
        try
        {
            _history = ConversationHistoryStore.CreateDefault();
            _history.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return null;
        }
        catch (Exception exception)
        {
            _history?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _history = null;
            return exception.Message;
        }
    }

    private void OnVoiceHotKeyPressed()
    {
        _window?.ShowPanel();
        if (_window is not null)
        {
            _ = _window.HandleVoiceHotKeyAsync();
        }
    }

    private static Task PlayReadyCueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SystemSounds.Asterisk.Play();
        return Task.CompletedTask;
    }

    private static CodexAppServerClient CreateCodexClient()
    {
        try
        {
            var processOptions = CodexProcessOptions.CreateDefault();
            return new CodexAppServerClient(new ProcessAppServerTransport(processOptions));
        }
        catch (Exception exception)
        {
            return new CodexAppServerClient(new UnavailableAppServerTransport(exception));
        }
    }
}
