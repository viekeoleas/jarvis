using System.Text;
using System.Text.Json;
using Jarvis.Core;

namespace Jarvis.Codex;

public sealed class CodexSession :
    IAssistantResponder,
    IConversationThreadDeleter,
    IAsyncDisposable
{
    private CodexAppServerClient _client;
    private readonly Func<CodexAppServerClient>? _clientFactory;
    private readonly string _workspace;
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private readonly object _activeTurnLock = new();
    private ActiveTurn? _activeTurn;
    private string? _threadId;
    private string? _pendingLoginId;
    private Task? _restartTask;
    private int _restartStarted;
    private bool _disposing;

    public CodexSession(CodexAppServerClient client, string workspace)
        : this(client, workspace, clientFactory: null)
    {
    }

    public CodexSession(Func<CodexAppServerClient> clientFactory, string workspace)
        : this(clientFactory(), workspace, clientFactory)
    {
    }

    private CodexSession(
        CodexAppServerClient client,
        string workspace,
        Func<CodexAppServerClient>? clientFactory)
    {
        _client = client;
        _clientFactory = clientFactory;
        _workspace = workspace;
        Subscribe(client);
    }

    public CodexAccountState AccountState { get; private set; } = CodexAccountState.Starting;

    public string? CurrentThreadId => _threadId;

    public event EventHandler<CodexAccountState>? AccountStateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_workspace);
            await _client.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await RefreshAccountAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SetAccountState(new CodexAccountState(
                CodexAccountPhase.Failed,
                exception.Message));
            throw;
        }
    }

    public async Task<ChatGptLoginPrompt> StartChatGptLoginAsync(CancellationToken cancellationToken)
    {
        var result = await _client.RequestAsync(
            "account/login/start",
            new
            {
                type = "chatgpt",
                useHostedLoginSuccessPage = true,
                appBrand = "chatgpt"
            },
            cancellationToken).ConfigureAwait(false);

        var loginId = result.GetProperty("loginId").GetString()
            ?? throw new CodexProtocolException("Codex did not return a login id.");
        var authUrl = result.GetProperty("authUrl").GetString()
            ?? throw new CodexProtocolException("Codex did not return an authorization URL.");

        _pendingLoginId = loginId;
        SetAccountState(new CodexAccountState(
            CodexAccountPhase.SigningIn,
            "Complete sign-in in your browser"));
        return new ChatGptLoginPrompt(loginId, new Uri(authUrl));
    }

    public async Task CancelLoginAsync(CancellationToken cancellationToken)
    {
        var loginId = _pendingLoginId;
        if (loginId is null)
        {
            return;
        }

        await _client.RequestAsync(
            "account/login/cancel",
            new { loginId },
            cancellationToken).ConfigureAwait(false);
        _pendingLoginId = null;
        SetAccountState(new CodexAccountState(CodexAccountPhase.SignedOut, "Sign-in cancelled"));
    }

    public async Task<string> RespondAsync(string request, CancellationToken cancellationToken)
    {
        if (AccountState.Phase != CodexAccountPhase.SignedIn ||
            !string.Equals(AccountState.AuthMode, "chatgpt", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Sign in with a ChatGPT Plus subscription before sending a request. API-key accounts are not supported.");
        }

        await _turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ActiveTurn? activeTurn = null;
        try
        {
            var threadId = await EnsureThreadAsync(cancellationToken).ConfigureAwait(false);
            activeTurn = new ActiveTurn(threadId);
            lock (_activeTurnLock)
            {
                _activeTurn = activeTurn;
            }

            var result = await _client.RequestAsync(
                "turn/start",
                new
                {
                    threadId,
                    input = new[] { new { type = "text", text = request } }
                },
                cancellationToken).ConfigureAwait(false);

            activeTurn.TurnId = result
                .GetProperty("turn")
                .GetProperty("id")
                .GetString();

            return await activeTurn.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryInterruptAsync(activeTurn).ConfigureAwait(false);
            throw;
        }
        finally
        {
            lock (_activeTurnLock)
            {
                _activeTurn = null;
            }

            _turnGate.Release();
        }
    }

    public async Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        try
        {
            await _client.RequestAsync(
                "thread/delete",
                new { threadId },
                cancellationToken).ConfigureAwait(false);
        }
        catch (CodexProtocolException exception) when (
            exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            // A previous cleanup may have deleted the remote thread before the local commit.
        }
        if (string.Equals(_threadId, threadId, StringComparison.Ordinal))
        {
            _threadId = null;
        }
    }

    private async Task TryInterruptAsync(ActiveTurn? activeTurn)
    {
        if (activeTurn?.TurnId is null)
        {
            return;
        }

        try
        {
            await _client.RequestAsync(
                "turn/interrupt",
                new { threadId = activeTurn.ThreadId, turnId = activeTurn.TurnId },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Cancellation must remain immediate even if the child process is already gone.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposing = true;
        if (_restartTask is not null)
        {
            await IgnoreFailureAsync(_restartTask).ConfigureAwait(false);
        }

        Unsubscribe(_client);
        _turnGate.Dispose();
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RefreshAccountAsync(CancellationToken cancellationToken)
    {
        var result = await _client.RequestAsync(
            "account/read",
            new { refreshToken = false },
            cancellationToken).ConfigureAwait(false);

        if (!result.TryGetProperty("account", out var account) ||
            account.ValueKind == JsonValueKind.Null)
        {
            SetAccountState(new CodexAccountState(
                CodexAccountPhase.SignedOut,
                "Sign in with ChatGPT Plus"));
            return;
        }

        SetAccountFromJson(account);
    }

    private async Task<string> EnsureThreadAsync(CancellationToken cancellationToken)
    {
        if (_threadId is not null)
        {
            return _threadId;
        }

        var result = await _client.RequestAsync(
            "thread/start",
            new
            {
                cwd = _workspace,
                approvalPolicy = "never",
                sandbox = "read-only",
                ephemeral = true,
                serviceName = "jarvis_windows"
            },
            cancellationToken).ConfigureAwait(false);

        _threadId = result
            .GetProperty("thread")
            .GetProperty("id")
            .GetString()
            ?? throw new CodexProtocolException("Codex did not return a thread id.");
        return _threadId;
    }

    private void OnNotificationReceived(object? sender, AppServerNotification notification)
    {
        switch (notification.Method)
        {
            case "account/updated":
                HandleAccountUpdated(notification.Params);
                break;
            case "account/login/completed":
                HandleLoginCompleted(notification.Params);
                break;
            case "item/agentMessage/delta":
                HandleAgentMessageDelta(notification.Params);
                break;
            case "item/completed":
                HandleItemCompleted(notification.Params);
                break;
            case "turn/completed":
                HandleTurnCompleted(notification.Params);
                break;
        }
    }

    private void HandleAccountUpdated(JsonElement parameters)
    {
        var authMode = GetNullableString(parameters, "authMode");
        var planType = GetNullableString(parameters, "planType");
        if (authMode is null)
        {
            SetAccountState(new CodexAccountState(
                CodexAccountPhase.SignedOut,
                "Sign in with ChatGPT Plus"));
            return;
        }

        if (!string.Equals(authMode, "chatgpt", StringComparison.Ordinal))
        {
            SetAccountState(new CodexAccountState(
                CodexAccountPhase.Failed,
                "Jarvis requires official ChatGPT sign-in; API-key mode is disabled.",
                authMode,
                planType));
            return;
        }

        SetAccountState(new CodexAccountState(
            CodexAccountPhase.SignedIn,
            $"ChatGPT {planType ?? "account"} connected",
            authMode,
            planType));
    }

    private void HandleLoginCompleted(JsonElement parameters)
    {
        _pendingLoginId = null;
        var success = parameters.TryGetProperty("success", out var successElement) &&
                      successElement.GetBoolean();
        if (!success)
        {
            var error = GetNullableString(parameters, "error") ?? "ChatGPT sign-in did not complete.";
            SetAccountState(new CodexAccountState(
                CodexAccountPhase.SignedOut,
                $"Sign-in failed: {error}"));
        }
    }

    private void HandleAgentMessageDelta(JsonElement parameters)
    {
        var threadId = GetNullableString(parameters, "threadId");
        var delta = GetNullableString(parameters, "delta");
        lock (_activeTurnLock)
        {
            if (_activeTurn is not null && _activeTurn.ThreadId == threadId && delta is not null)
            {
                _activeTurn.Response.Append(delta);
            }
        }
    }

    private void HandleItemCompleted(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("item", out var item) ||
            GetNullableString(item, "type") != "agentMessage")
        {
            return;
        }

        var threadId = GetNullableString(parameters, "threadId");
        var text = GetNullableString(item, "text");
        lock (_activeTurnLock)
        {
            if (_activeTurn is not null &&
                _activeTurn.ThreadId == threadId &&
                _activeTurn.Response.Length == 0 &&
                text is not null)
            {
                _activeTurn.Response.Append(text);
            }
        }
    }

    private void HandleTurnCompleted(JsonElement parameters)
    {
        ActiveTurn? activeTurn;
        lock (_activeTurnLock)
        {
            activeTurn = _activeTurn;
        }

        if (activeTurn is null || activeTurn.ThreadId != GetNullableString(parameters, "threadId"))
        {
            return;
        }

        var turn = parameters.GetProperty("turn");
        var turnId = GetNullableString(turn, "id");
        if (activeTurn.TurnId is not null && activeTurn.TurnId != turnId)
        {
            return;
        }

        var status = GetNullableString(turn, "status");
        if (status == "completed")
        {
            var response = activeTurn.Response.ToString().Trim();
            activeTurn.Completion.TrySetResult(
                string.IsNullOrWhiteSpace(response) ? "Codex completed without a text response." : response);
            return;
        }

        var error = turn.TryGetProperty("error", out var errorElement) &&
                    errorElement.ValueKind == JsonValueKind.Object
            ? GetNullableString(errorElement, "message")
            : null;
        activeTurn.Completion.TrySetException(new InvalidOperationException(
            error ?? $"Codex turn ended with status {status ?? "unknown"}."));
    }

    private void OnConnectionLost(object? sender, AppServerTransportExitedEventArgs e)
    {
        var exception = e.Exception ?? new InvalidOperationException("Codex connection was lost.");
        lock (_activeTurnLock)
        {
            _activeTurn?.Completion.TrySetException(exception);
        }

        if (_disposing ||
            _clientFactory is null ||
            Interlocked.CompareExchange(ref _restartStarted, 1, 0) != 0)
        {
            SetAccountState(new CodexAccountState(CodexAccountPhase.Failed, exception.Message));
            return;
        }

        SetAccountState(new CodexAccountState(
            CodexAccountPhase.Restarting,
            "Codex stopped; restarting once"));
        _restartTask = RestartOnceAsync();
    }

    private async Task RestartOnceAsync()
    {
        try
        {
            var previousClient = _client;
            Unsubscribe(previousClient);
            await previousClient.DisposeAsync().ConfigureAwait(false);
            if (_disposing)
            {
                return;
            }

            var replacementClient = _clientFactory!();
            _client = replacementClient;
            _threadId = null;
            _pendingLoginId = null;
            Subscribe(replacementClient);
            await replacementClient.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
            await RefreshAccountAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!_disposing)
            {
                SetAccountState(new CodexAccountState(CodexAccountPhase.Failed, exception.Message));
            }
        }
    }

    private void SetAccountFromJson(JsonElement account)
    {
        var authMode = GetNullableString(account, "type");
        var planType = GetNullableString(account, "planType");
        var email = GetNullableString(account, "email");

        if (authMode != "chatgpt")
        {
            SetAccountState(new CodexAccountState(
                CodexAccountPhase.Failed,
                "Jarvis requires ChatGPT subscription sign-in; API-key mode is disabled.",
                authMode,
                planType,
                email));
            return;
        }

        SetAccountState(new CodexAccountState(
            CodexAccountPhase.SignedIn,
            $"ChatGPT {planType ?? "account"} connected",
            authMode,
            planType,
            email));
    }

    private void SetAccountState(CodexAccountState state)
    {
        AccountState = state;
        AccountStateChanged?.Invoke(this, state);
    }

    private void Subscribe(CodexAppServerClient client)
    {
        client.NotificationReceived += OnNotificationReceived;
        client.ConnectionLost += OnConnectionLost;
    }

    private void Unsubscribe(CodexAppServerClient client)
    {
        client.NotificationReceived -= OnNotificationReceived;
        client.ConnectionLost -= OnConnectionLost;
    }

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Restart failures are already converted to a safe account state.
        }
    }

    private static string? GetNullableString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.GetString();
    }

    private sealed class ActiveTurn(string threadId)
    {
        public string ThreadId { get; } = threadId;

        public string? TurnId { get; set; }

        public StringBuilder Response { get; } = new();

        public TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
