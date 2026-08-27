using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Jarvis.Core;

namespace Jarvis.App;

public partial class MainWindow : Window, INotifyPropertyChanged, IJarvisPanel
{
    private readonly JarvisController _controller;
    private bool _allowClose;
    private string _request = "System check";
    private string _status = AssistantViewState.Initial.Status;
    private string _output = "Jarvis is running locally. No microphone, network, or persistent storage is active in this tracer.";

    public MainWindow(JarvisController controller)
    {
        _controller = controller;
        _controller.StateChanged += OnStateChanged;
        DataContext = this;
        InitializeComponent();
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

    private void OnStateChanged(object? sender, AssistantViewState state)
    {
        Dispatcher.Invoke(() =>
        {
            Status = state.Status;
            Output = state.Error ?? state.Response ?? state.Request ?? "Ready";
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
