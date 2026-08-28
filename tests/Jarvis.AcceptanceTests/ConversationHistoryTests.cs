using Jarvis.Core;
using Jarvis.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jarvis.AcceptanceTests;

public sealed class ConversationHistoryTests
{
    [Fact]
    public async Task Text_turn_survives_restart_with_explicit_seven_day_deadlines()
    {
        var fixture = new HistoryFixture();
        try
        {
            var clock = new ManualTimeProvider(
                new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero));
            await using (var first = fixture.CreateStore(clock))
            {
                await first.InitializeAsync(TestContext.Current.CancellationToken);
                var responder = new HistoryRecordingResponder(
                    new StubResponder("Готово."),
                    first,
                    () => "thread-persisted");
                await responder.RespondAsync(
                    "Открой блокнот.",
                    TestContext.Current.CancellationToken);
            }

            await using var restarted = fixture.CreateStore(clock);
            await restarted.InitializeAsync(TestContext.Current.CancellationToken);
            var messages = await restarted.GetRecentMessagesAsync(
                10,
                TestContext.Current.CancellationToken);

            Assert.Equal(2, messages.Count);
            Assert.Equal(["Готово.", "Открой блокнот."], messages.Select(message => message.Text));
            Assert.All(messages, message =>
                Assert.Equal(clock.GetUtcNow().AddDays(7), message.ExpiresUtc));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Cleanup_removes_text_immediately_and_retries_Codex_tombstone_safely()
    {
        var fixture = new HistoryFixture();
        try
        {
            var clock = new ManualTimeProvider(
                new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
            await using var store = fixture.CreateStore(clock);
            await store.InitializeAsync(TestContext.Current.CancellationToken);
            var conversationId = await store.AppendTurnAsync(
                "thread-retry",
                "Private request text",
                "Private response text",
                TestContext.Current.CancellationToken);
            await store.AppendActionAsync(
                conversationId,
                "Opened Notepad",
                TestContext.Current.CancellationToken);
            await store.AddLongTermMemoryAsync(
                "Preferred language is Russian",
                TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromDays(8));
            var deleter = new FlakyThreadDeleter();
            var cleanup = new RetentionCleanupService(store, deleter);

            var first = await cleanup.RunAsync(TestContext.Current.CancellationToken);

            Assert.Equal(3, first.LocalTextRecordsDeleted);
            Assert.Equal(1, first.ThreadDeletionsPending);
            Assert.Empty(await store.GetRecentMessagesAsync(
                10,
                TestContext.Current.CancellationToken));
            Assert.Equal(1, await store.CountLongTermMemoriesAsync(
                TestContext.Current.CancellationToken));

            deleter.Fail = false;
            var second = await cleanup.RunAsync(TestContext.Current.CancellationToken);
            var third = await cleanup.RunAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, second.ThreadTombstonesDeleted);
            Assert.Equal(0, second.ThreadDeletionsPending);
            Assert.Equal(new CleanupResult(0, 0, 0), third);
            Assert.Equal(2, deleter.Attempts);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Database_schema_has_no_audio_screenshot_oauth_or_secret_columns()
    {
        var fixture = new HistoryFixture();
        try
        {
            await using var store = fixture.CreateStore(TimeProvider.System);
            await store.InitializeAsync(TestContext.Current.CancellationToken);
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = fixture.DatabasePath,
                Pooling = false
            };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table';";
            await using var reader = await command.ExecuteReaderAsync(
                TestContext.Current.CancellationToken);
            var schema = new List<string>();
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    schema.Add(reader.GetString(0));
                }
            }

            var joined = string.Join("\n", schema);
            Assert.DoesNotContain("audio", joined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("screenshot", joined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("oauth", joined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", joined, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private sealed class HistoryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "JarvisAcceptance",
            Guid.NewGuid().ToString("N"));

        public string DatabasePath => Path.Combine(_root, "history.db");

        public ConversationHistoryStore CreateStore(TimeProvider clock) =>
            new(DatabasePath, clock, TimeSpan.FromDays(7));

        public void Dispose()
        {
            var fullRoot = Path.GetFullPath(_root);
            var expectedParent = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "JarvisAcceptance")) + Path.DirectorySeparatorChar;
            if (fullRoot.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class StubResponder(string response) : IAssistantResponder
    {
        public Task<string> RespondAsync(string request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class FlakyThreadDeleter : IConversationThreadDeleter
    {
        public bool Fail { get; set; } = true;

        public int Attempts { get; private set; }

        public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Fail)
            {
                throw new InvalidOperationException("Codex temporarily unavailable");
            }

            return Task.CompletedTask;
        }
    }
}
