using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using Jarvis.Codex;
using Jarvis.Core;

namespace Jarvis.App;

public partial class MainWindow : Window, INotifyPropertyChanged, IJarvisPanel
{
    private readonly JarvisController _controller;
    private readonly CodexSession _codexSession;
    private bool _allowClose;
    private string _request = "Introduce yourself in one sentence.";
    private string _status = AssistantViewState.Initial.Status;
    private string _output = "Connect your ChatGPT Plus account to begin.";
    private string _accountStatus = CodexAccountState.Starting.Message;
    private string _signInButtonText = "Sign in";
    private bool _isSignInEnabled;
    private bool _canSend;

    public MainWindow(JarvisController controller, CodexSession codexSession)
    {
        _controller = controller;
        _codexSession = codexSession;
        _controller.StateChanged += OnStateChanged;
        _codexSession.AccountStateChanged += OnAccountStateChanged;
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
