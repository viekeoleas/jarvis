using System.IO;
using System.Threading;
using System.Windows;
using Jarvis.Codex;
using Jarvis.Core;
using Jarvis.Speech;

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

        var speechUnavailableReason = TryCreateSpeechController();
        var controller = new JarvisController(_codexSession);
        _window = new MainWindow(
            controller,
            _codexSession,
            _speechController,
            speechUnavailableReason);
        _panelController = new PanelController(_window);
        _trayHost = new TrayHost(_window, _panelController.TogglePanel, ExitApplication);
        _hotKey = new GlobalHotKey(_window, OnVoiceHotKeyPressed);
        _ = InitializeCodexAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKey?.Dispose();
        _trayHost?.Dispose();
        _speechController?.Dispose();
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
    }

    private void ExitApplication()
    {
        _window?.AllowClose();
        Shutdown();
    }

    private string? TryCreateSpeechController()
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
                new VoicePersonaResponder(_codexSession!));
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

    private void OnVoiceHotKeyPressed()
    {
        _window?.ShowPanel();
        if (_window is not null)
        {
            _ = _window.ToggleSpeechAsync();
        }
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
