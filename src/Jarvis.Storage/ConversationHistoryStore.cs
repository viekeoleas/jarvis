using Microsoft.Data.Sqlite;

namespace Jarvis.Storage;

public sealed class ConversationHistoryStore : IAsyncDisposable
{
    private const int SchemaVersion = 1;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;

    public ConversationHistoryStore(
        string databasePath,
        TimeProvider timeProvider,
        TimeSpan retention)
    {
        DatabasePath = databasePath;
        _timeProvider = timeProvider;
        _retention = retention;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public string DatabasePath { get; }

    public static ConversationHistoryStore CreateDefault()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new ConversationHistoryStore(
            Path.Combine(localData, "Jarvis", "Data", "jarvis.db"),
            TimeProvider.System,
            TimeSpan.FromDays(7));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException("History database directory could not be resolved.");
        Directory.CreateDirectory(directory);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var version = await ReadSchemaVersionAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            if (version < 1)
            {
                await ExecuteAsync(connection, transaction, """
                    CREATE TABLE conversations (
                        id TEXT PRIMARY KEY,
                        codex_thread_id TEXT UNIQUE,
                        created_utc INTEGER NOT NULL,
                        expires_utc INTEGER NOT NULL,
                        deletion_state TEXT NOT NULL DEFAULT 'active',
                        deletion_attempts INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE TABLE messages (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                        role TEXT NOT NULL CHECK(role IN ('user', 'assistant')),
                        text TEXT NOT NULL,
                        created_utc INTEGER NOT NULL,
                        expires_utc INTEGER NOT NULL
                    );
                    CREATE INDEX messages_expiry ON messages(expires_utc);
                    CREATE TABLE action_journal (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        conversation_id TEXT REFERENCES conversations(id) ON DELETE SET NULL,
                        summary TEXT NOT NULL,
                        created_utc INTEGER NOT NULL,
                        expires_utc INTEGER NOT NULL
                    );
                    CREATE INDEX action_journal_expiry ON action_journal(expires_utc);
                    CREATE TABLE long_term_memories (
                        id TEXT PRIMARY KEY,
                        content TEXT NOT NULL,
                        created_utc INTEGER NOT NULL,
                        updated_utc INTEGER NOT NULL
                    );
                    PRAGMA user_version = 1;
                    """, cancellationToken).ConfigureAwait(false);
            }

            if (version > SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"History schema {version} is newer than supported version {SchemaVersion}.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> AppendTurnAsync(
        string? codexThreadId,
        string userText,
        string assistantText,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);
        ArgumentException.ThrowIfNullOrWhiteSpace(assistantText);
        var now = _timeProvider.GetUtcNow();
        var expires = now.Add(_retention);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var conversationId = codexThreadId is null
                ? Guid.NewGuid().ToString("N")
                : await FindConversationIdAsync(
                    connection, transaction, codexThreadId, cancellationToken).ConfigureAwait(false)
                    ?? Guid.NewGuid().ToString("N");

            await using (var conversation = connection.CreateCommand())
            {
                conversation.Transaction = transaction;
                conversation.CommandText = """
                    INSERT INTO conversations
                        (id, codex_thread_id, created_utc, expires_utc, deletion_state)
                    VALUES ($id, $thread, $created, $expires, 'active')
                    ON CONFLICT(id) DO UPDATE SET
                        expires_utc = excluded.expires_utc,
                        deletion_state = 'active';
                    """;
                conversation.Parameters.AddWithValue("$id", conversationId);
                conversation.Parameters.AddWithValue("$thread", (object?)codexThreadId ?? DBNull.Value);
                conversation.Parameters.AddWithValue("$created", now.ToUnixTimeMilliseconds());
                conversation.Parameters.AddWithValue("$expires", expires.ToUnixTimeMilliseconds());
                await conversation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await InsertMessageAsync(
                connection, transaction, conversationId, "user", userText, now, expires, cancellationToken)
                .ConfigureAwait(false);
            await InsertMessageAsync(
                connection, transaction, conversationId, "assistant", assistantText, now, expires, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return conversationId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendActionAsync(
        string? conversationId,
        string summary,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        var now = _timeProvider.GetUtcNow();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO action_journal
                    (conversation_id, summary, created_utc, expires_utc)
                VALUES ($conversation, $summary, $created, $expires);
                """;
            command.Parameters.AddWithValue("$conversation", (object?)conversationId ?? DBNull.Value);
            command.Parameters.AddWithValue("$summary", summary);
            command.Parameters.AddWithValue("$created", now.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$expires", now.Add(_retention).ToUnixTimeMilliseconds());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddLongTermMemoryAsync(string content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO long_term_memories (id, content, created_utc, updated_utc)
                VALUES ($id, $content, $now, $now);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$content", content);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT m.conversation_id, m.role, m.text, m.created_utc, m.expires_utc
                FROM messages m
                JOIN conversations c ON c.id = m.conversation_id
                WHERE c.deletion_state = 'active'
                    AND m.expires_utc > $now
                    AND c.expires_utc > $now
                ORDER BY m.created_utc DESC, m.id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue(
                "$now",
                _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            var messages = new List<ConversationMessage>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messages.Add(new ConversationMessage(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4))));
            }

            return messages;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(IReadOnlyList<ExpiredConversation> Conversations, int DeletedTextRecords)>
        PruneExpiredTextAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var deleted = 0;
            deleted += await DeleteExpiredAsync(
                connection, transaction, "messages", now, cancellationToken).ConfigureAwait(false);
            deleted += await DeleteExpiredAsync(
                connection, transaction, "action_journal", now, cancellationToken).ConfigureAwait(false);
            await using (var mark = connection.CreateCommand())
            {
                mark.Transaction = transaction;
                mark.CommandText = """
                    UPDATE conversations
                    SET deletion_state = 'pending'
                    WHERE expires_utc <= $now;
                    """;
                mark.Parameters.AddWithValue("$now", now);
                await mark.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var expired = new List<ExpiredConversation>();
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = """
                    SELECT id, codex_thread_id
                    FROM conversations
                    WHERE deletion_state = 'pending';
                    """;
                await using var reader = await select.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    expired.Add(new ExpiredConversation(
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1)));
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return (expired, deleted);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteThreadDeletionAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        await ExecuteConversationCommandAsync(
            "DELETE FROM conversations WHERE id = $id AND deletion_state = 'pending';",
            conversationId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkThreadDeletionFailedAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        await ExecuteConversationCommandAsync(
            """
            UPDATE conversations
            SET deletion_attempts = deletion_attempts + 1
            WHERE id = $id AND deletion_state = 'pending';
            """,
            conversationId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountLongTermMemoriesAsync(CancellationToken cancellationToken) =>
        await ExecuteScalarIntAsync(
            "SELECT COUNT(*) FROM long_term_memories;",
            cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        _gate.Dispose();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> FindConversationIdAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string threadId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT id FROM conversations WHERE codex_thread_id = $thread;";
        command.Parameters.AddWithValue("$thread", threadId);
        return (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertMessageAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string conversationId,
        string role,
        string text,
        DateTimeOffset created,
        DateTimeOffset expires,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO messages
                (conversation_id, role, text, created_utc, expires_utc)
            VALUES ($conversation, $role, $text, $created, $expires);
            """;
        command.Parameters.AddWithValue("$conversation", conversationId);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$created", created.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$expires", expires.ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> DeleteExpiredAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string table,
        long now,
        CancellationToken cancellationToken)
    {
        if (table is not ("messages" or "action_journal"))
        {
            throw new ArgumentOutOfRangeException(nameof(table));
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = $"DELETE FROM {table} WHERE expires_utc <= $now;";
        command.Parameters.AddWithValue("$now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteConversationCommandAsync(
        string sql,
        string conversationId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", conversationId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> ExecuteScalarIntAsync(string sql, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }
}
