using System.Text.Json;
using Jarvis.Codex;
using Xunit;

namespace Jarvis.AcceptanceTests;

public sealed class CodexSessionTests
{
    [Fact]
    public async Task Initialization_performs_the_required_handshake_and_reports_signed_out()
    {
        var transport = CreateScriptedTransport(account: null);
        await using var session = CreateSession(transport);

        await session.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(transport.Started);
        Assert.Equal(CodexAccountPhase.SignedOut, session.AccountState.Phase);
        Assert.Equal(
            ["initialize", "initialized", "account/read"],
            transport.SentMessages.Select(GetMethod));
    }

    [Fact]
    public async Task Browser_login_uses_managed_ChatGPT_auth_and_accepts_a_Plus_update()
    {
        var transport = CreateScriptedTransport(account: null);
        await using var session = CreateSession(transport);
        await session.InitializeAsync(TestContext.Current.CancellationToken);

        var prompt = await session.StartChatGptLoginAsync(TestContext.Current.CancellationToken);
        transport.Deliver(new
        {
            method = "account/login/completed",
            @params = new { loginId = "login-1", success = true, error = (string?)null }
        });
        transport.Deliver(new
        {
            method = "account/updated",
            @params = new { authMode = "chatgpt", planType = "plus" }
        });

        Assert.Equal("login-1", prompt.LoginId);
        Assert.Equal("https://chatgpt.com/authorize", prompt.AuthorizationUri.AbsoluteUri);
        Assert.Equal(CodexAccountPhase.SignedIn, session.AccountState.Phase);
        Assert.Equal("chatgpt", session.AccountState.AuthMode);
        Assert.Equal("plus", session.AccountState.PlanType);
        var loginRequest = transport.SentMessages.Single(message => GetMethod(message) == "account/login/start");
        Assert.Equal("chatgpt", loginRequest.GetProperty("params").GetProperty("type").GetString());
        Assert.False(loginRequest.GetRawText().Contains("apiKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Pending_browser_login_can_be_cancelled()
    {
        var transport = CreateScriptedTransport(account: null);
        await using var session = CreateSession(transport);
        await session.InitializeAsync(TestContext.Current.CancellationToken);
        await session.StartChatGptLoginAsync(TestContext.Current.CancellationToken);

        await session.CancelLoginAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CodexAccountPhase.SignedOut, session.AccountState.Phase);
        Assert.Contains(
            transport.SentMessages,
            message => GetMethod(message) == "account/login/cancel");
    }

    [Fact]
    public async Task Failed_browser_login_reports_the_error_and_allows_a_retry()
    {
        var transport = CreateScriptedTransport(account: null);
        await using var session = CreateSession(transport);
        await session.InitializeAsync(TestContext.Current.CancellationToken);
        await session.StartChatGptLoginAsync(TestContext.Current.CancellationToken);

        transport.Deliver(new
        {
            method = "account/login/completed",
            @params = new { loginId = "login-1", success = false, error = "Browser closed" }
        });

        Assert.Equal(CodexAccountPhase.SignedOut, session.AccountState.Phase);
        Assert.Contains("Browser closed", session.AccountState.Message);
    }

    [Fact]
    public async Task Text_turn_streams_the_final_agent_message_through_the_application_seam()
    {
        var transport = CreateScriptedTransport(new
        {
            type = "chatgpt",
            email = "user@example.com",
            planType = "plus"
        });
        await using var session = CreateSession(transport);
        await session.InitializeAsync(TestContext.Current.CancellationToken);

        var response = await session.RespondAsync(
            "Introduce yourself.",
            TestContext.Current.CancellationToken);

        Assert.Equal("At your service, sir.", response);
        Assert.Contains(transport.SentMessages, message => GetMethod(message) == "thread/start");
        var turn = transport.SentMessages.Single(message => GetMethod(message) == "turn/start");
        Assert.Equal(
            "Introduce yourself.",
            turn.GetProperty("params").GetProperty("input")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Api_key_account_is_rejected_without_exposing_the_key()
    {
        var transport = CreateScriptedTransport(new { type = "apiKey" });
        await using var session = CreateSession(transport);

        await session.InitializeAsync(TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.RespondAsync("Hello", TestContext.Current.CancellationToken));

        Assert.Equal(CodexAccountPhase.Failed, session.AccountState.Phase);
        Assert.Contains("subscription", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Process_exit_fails_an_active_turn_and_updates_the_account_state()
    {
        var transport = CreateScriptedTransport(
            new { type = "chatgpt", email = (string?)null, planType = "plus" },
            completeTurn: false);
        await using var session = CreateSession(transport);
        await session.InitializeAsync(TestContext.Current.CancellationToken);

        var responseTask = session.RespondAsync("Wait", TestContext.Current.CancellationToken);
        await WaitUntilAsync(
            () => transport.SentMessages.Any(message => GetMethod(message) == "turn/start"),
            TestContext.Current.CancellationToken);
        transport.Exit(new InvalidOperationException("Codex stopped"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => responseTask);
        Assert.Equal("Codex stopped", exception.Message);
        Assert.Equal(CodexAccountPhase.Failed, session.AccountState.Phase);
    }

    [Fact]
    public async Task Process_exit_restarts_once_and_does_not_enter_a_restart_loop()
    {
        var firstTransport = CreateScriptedTransport(
            new { type = "chatgpt", email = (string?)null, planType = "plus" });
        var secondTransport = CreateScriptedTransport(
            new { type = "chatgpt", email = (string?)null, planType = "plus" });
        var transports = new Queue<FakeAppServerTransport>([firstTransport, secondTransport]);
        var clientsCreated = 0;
        await using var session = new CodexSession(
            () =>
            {
                clientsCreated++;
                return new CodexAppServerClient(transports.Dequeue());
            },
            Path.GetTempPath());
        await session.InitializeAsync(TestContext.Current.CancellationToken);

        firstTransport.Exit(new InvalidOperationException("First crash"));
        await WaitUntilAsync(
            () => secondTransport.Started && session.AccountState.Phase == CodexAccountPhase.SignedIn,
            TestContext.Current.CancellationToken);

        Assert.True(firstTransport.Disposed);
        Assert.Equal(2, clientsCreated);
        secondTransport.Exit(new InvalidOperationException("Second crash"));
        Assert.Equal(CodexAccountPhase.Failed, session.AccountState.Phase);
        Assert.Equal(2, clientsCreated);
    }

    [Fact]
    public async Task Cancelling_an_active_request_releases_the_session_for_another_turn()
    {
        var transport = CreateScriptedTransport(
            new { type = "chatgpt", email = (string?)null, planType = "plus" },
            completeTurn: false);
        await using var session = CreateSession(transport);
        await session.InitializeAsync(TestContext.Current.CancellationToken);
        using var requestCancellation = new CancellationTokenSource();

        var responseTask = session.RespondAsync("Wait", requestCancellation.Token);
        await WaitUntilAsync(
            () => transport.SentMessages.Any(message => GetMethod(message) == "turn/start"),
            TestContext.Current.CancellationToken);
        requestCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => responseTask);
        Assert.Contains(
            transport.SentMessages,
            message => GetMethod(message) == "turn/interrupt");
        transport.CompleteTurn = true;
        var response = await session.RespondAsync("Continue", TestContext.Current.CancellationToken);
        Assert.Equal("At your service, sir.", response);
    }

    private static CodexSession CreateSession(FakeAppServerTransport transport)
    {
        var client = new CodexAppServerClient(transport);
        return new CodexSession(client, Path.GetTempPath());
    }

    internal static FakeAppServerTransport CreateScriptedTransport(
        object? account,
        bool completeTurn = true)
    {
        var transport = new FakeAppServerTransport();
        transport.CompleteTurn = completeTurn;
        transport.MessageHandler = message =>
        {
            var method = GetMethod(message);
            if (!message.TryGetProperty("id", out var id))
            {
                return Task.CompletedTask;
            }

            switch (method)
            {
                case "initialize":
                    transport.Deliver(new
                    {
                        id = id.GetInt64(),
                        result = new
                        {
                            userAgent = "codex_cli_rs/0.150.0-alpha.8",
                            platformFamily = "windows",
                            platformOs = "windows",
                            codexHome = "C:\\Jarvis\\Codex"
                        }
                    });
                    break;
                case "account/read":
                    transport.Deliver(new
                    {
                        id = id.GetInt64(),
                        result = new { account, requiresOpenaiAuth = true }
                    });
                    break;
                case "account/login/start":
                    transport.Deliver(new
                    {
                        id = id.GetInt64(),
                        result = new
                        {
                            type = "chatgpt",
                            loginId = "login-1",
                            authUrl = "https://chatgpt.com/authorize"
                        }
                    });
                    break;
                case "account/login/cancel":
                    transport.Deliver(new { id = id.GetInt64(), result = new { } });
                    break;
                case "thread/start":
                    transport.Deliver(new
                    {
                        id = id.GetInt64(),
                        result = new { thread = new { id = "thread-1" } }
                    });
                    break;
                case "turn/start":
                    transport.Deliver(new
                    {
                        id = id.GetInt64(),
                        result = new { turn = new { id = "turn-1", status = "inProgress", items = Array.Empty<object>() } }
                    });
                    if (transport.CompleteTurn)
                    {
                        transport.Deliver(new
                        {
                            method = "item/agentMessage/delta",
                            @params = new
                            {
                                threadId = "thread-1",
                                turnId = "turn-1",
                                itemId = "item-1",
                                delta = "At your service, sir."
                            }
                        });
                        transport.Deliver(new
                        {
                            method = "turn/completed",
                            @params = new
                            {
                                threadId = "thread-1",
                                turn = new
                                {
                                    id = "turn-1",
                                    status = "completed",
                                    items = Array.Empty<object>(),
                                    error = (object?)null
                                }
                            }
                        });
                    }
                    break;
                case "turn/interrupt":
                    transport.Deliver(new { id = id.GetInt64(), result = new { } });
                    break;
            }

            return Task.CompletedTask;
        };
        return transport;
    }

    internal static string? GetMethod(JsonElement message) =>
        message.TryGetProperty("method", out var method) ? method.GetString() : null;

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }
}
