using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using Jarvis.Codex;
using Jarvis.Core;
using Jarvis.Speech;
using Jarvis.Storage;

namespace Jarvis.App;

public partial class MainWindow : Window, INotifyPropertyChanged, IJarvisPanel
{
    private readonly JarvisController _controller;
    private readonly CodexSession _codexSession;
    private readonly LocalSpeechTurnController? _speechController;
    private readonly ConversationHistoryStore? _history;
    private readonly string? _historyUnavailableReason;
    private readonly HandsFreeSessionController? _handsFree;
    private bool _allowClose;
    private string _request = "Introduce yourself in one sentence.";
    private string _status = AssistantViewState.Initial.Status;
    private string _output = "Connect your ChatGPT Plus account to begin.";
    private string _accountStatus = CodexAccountState.Starting.Message;
    private string _signInButtonText = "Sign in";
    private bool _isSignInEnabled;
    private bool _canSend;
    private string _voiceStatus = "Local speech ready";
    private string _voiceButtonText = "Start listening";
    private bool _canToggleVoice = true;

    public MainWindow(
        JarvisController controller,
        CodexSession codexSession,
        LocalSpeechTurnController? speechController = null,
        string? speechUnavailableReason = null,
        ConversationHistoryStore? history = null,
        string? historyUnavailableReason = null,
        HandsFreeSessionController? handsFree = null)
    {
        _controller = controller;
        _codexSession = codexSession;
        _speechController = speechController;
        _history = history;
        _historyUnavailableReason = historyUnavailableReason;
        _handsFree = handsFree;
        _controller.StateChanged += OnStateChanged;
        _codexSession.AccountStateChanged += OnAccountStateChanged;
        if (_speechController is not null)
        {
            _speechController.StateChanged += OnSpeechStateChanged;
        }

        if (_handsFree is not null)
        {
            _handsFree.StateChanged += OnHandsFreeStateChanged;
        }

        if (_speechController is null)
        {
            _voiceStatus = speechUnavailableReason ?? "Local speech is unavailable";
            _voiceButtonText = "Unavailable";
            _canToggleVoice = false;
        }

        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Request
    {
        get => _request;
        set => SetField(ref _request, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string Output
    {
        get => _output;
        private set => SetField(ref _output, value);
    }

    public string AccountStatus
    {
        get => _accountStatus;
        private set => SetField(ref _accountStatus, value);
    }

    public string SignInButtonText
    {
        get => _signInButtonText;
        private set => SetField(ref _signInButtonText, value);
    }

    public bool IsSignInEnabled
    {
        get => _isSignInEnabled;
        private set => SetField(ref _isSignInEnabled, value);
    }

    public bool CanSend
    {
        get => _canSend;
        private set => SetField(ref _canSend, value);
    }

    public string VoiceStatus
    {
        get => _voiceStatus;
        private set => SetField(ref _voiceStatus, value);
    }

    public string VoiceButtonText
    {
        get => _voiceButtonText;
        private set => SetField(ref _voiceButtonText, value);
    }

    public bool CanToggleVoice
    {
        get => _canToggleVoice;
        private set => SetField(ref _canToggleVoice, value);
    }

    public bool IsPanelVisible => IsVisible;

    public void ShowPanel()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void HidePanel() => Hide();

    public void AllowClose()
    {
        _allowClose = true;
        Close();
    }

    public async Task ToggleSpeechAsync()
    {
        if (_speechController is null)
        {
            Status = "Speech unavailable";
            Output = VoiceStatus;
            return;
        }

        await _speechController.ToggleAsync();
    }

    public async Task HandleVoiceHotKeyAsync()
    {
        if (_handsFree is not null)
        {
            await _handsFree.HandleHotKeyAsync();
            return;
        }

        await ToggleSpeechAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private async void RunTracer_Click(object sender, RoutedEventArgs e)
    {
        await _controller.SubmitAsync(Request);
    }

    private async void ToggleSpeech_Click(object sender, RoutedEventArgs e)
    {
        await HandleVoiceHotKeyAsync();
    }

    private async void EnrollWake_Click(object sender, RoutedEventArgs e)
    {
        if (_handsFree is null)
        {
            Status = "Wake phrase unavailable";
            return;
        }

        await _handsFree.EnrollAsync(CancellationToken.None);
    }

    private async void History_Click(object sender, RoutedEventArgs e)
    {
        if (_history is null)
        {
            Status = "History unavailable";
            Output = _historyUnavailableReason ?? "Local history is unavailable.";
            return;
        }

        try
        {
            var messages = await _history.GetRecentMessagesAsync(20, CancellationToken.None);
            Status = "Recent history";
            Output = messages.Count == 0
                ? "No retained conversations."
                : string.Join(
                    Environment.NewLine + Environment.NewLine,
                    messages.Reverse().Select(message =>
                        $"{(message.Role == "user" ? "You" : "Jarvis")}: {message.Text}"));
        }
        catch (Exception exception)
        {
            Status = "History failed";
            Output = exception.Message;
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_codexSession.AccountState.Phase == CodexAccountPhase.SigningIn)
            {
                await _codexSession.CancelLoginAsync(CancellationToken.None);
                return;
            }

            var prompt = await _codexSession.StartChatGptLoginAsync(CancellationToken.None);
            Process.Start(new ProcessStartInfo(prompt.AuthorizationUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Status = "Sign-in failed";
            Output = exception.Message;
        }
    }

    private void OnStateChanged(object? sender, AssistantViewState state)
    {
        Dispatcher.Invoke(() =>
        {
            Status = state.Status;
            Output = state.Error ?? state.Response ?? state.Request ?? "Ready";
        });
    }

    private void OnAccountStateChanged(object? sender, CodexAccountState state)
    {
        Dispatcher.Invoke(() =>
        {
            AccountStatus = state.Message;
            CanSend = state.Phase == CodexAccountPhase.SignedIn;
            IsSignInEnabled = state.Phase is CodexAccountPhase.SignedOut or
                CodexAccountPhase.SigningIn;
            SignInButtonText = state.Phase switch
            {
                CodexAccountPhase.SigningIn => "Cancel",
                CodexAccountPhase.Restarting => "Retrying",
                CodexAccountPhase.SignedIn => "Connected",
                CodexAccountPhase.Failed => "Unavailable",
                CodexAccountPhase.Starting => "Connecting",
                _ => "Sign in"
            };
        });
    }

    private void OnSpeechStateChanged(object? sender, SpeechTurnState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            VoiceStatus = BuildVoiceStatus(state);
            VoiceButtonText = state.Phase switch
            {
                SpeechTurnPhase.Listening => "Stop and transcribe",
                SpeechTurnPhase.Transcribing or SpeechTurnPhase.Thinking or
                    SpeechTurnPhase.Speaking => "Cancel",
                _ => "Start listening"
            };
            CanToggleVoice = true;
            Status = state.Status;
            if (state.Transcript is not null)
            {
                Request = state.Transcript;
            }

            Output = state.Error ?? state.Response ?? state.Transcript ?? state.Status;
        });
    }

    private void OnHandsFreeStateChanged(object? sender, HandsFreeState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            VoiceStatus = state.Error ?? state.Status;
            CanToggleVoice = state.Phase != HandsFreePhase.Enrolling;
            if (state.Phase == HandsFreePhase.Failed)
            {
                Status = "Wake phrase unavailable";
            }
        });
    }

    private static string BuildVoiceStatus(SpeechTurnState state)
    {
        if (state.Error is not null)
        {
            return state.Error;
        }

        var timings = new List<string>();
        if (state.Backend is not null)
        {
            timings.Add(state.Backend.ToString()!);
        }

        if (state.TranscriptionElapsed is not null)
        {
            timings.Add($"STT {state.TranscriptionElapsed.Value.TotalSeconds:0.0}s");
        }

        if (state.SynthesisElapsed is not null)
        {
            timings.Add($"TTS {state.SynthesisElapsed.Value.TotalSeconds:0.0}s");
        }

        return timings.Count == 0
            ? state.Status
            : $"{state.Status} · {string.Join(" · ", timings)}";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
