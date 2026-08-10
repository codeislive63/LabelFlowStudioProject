using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Statistics;
using Microsoft.Data.Sqlite;

namespace LabelFlowStudio.Data.Statistics;

/// <summary>
/// Хранит завершённые автоматические обработки в локальной SQLite-базе.
/// </summary>
public sealed class SqliteAutomaticProcessingHistoryStore : IAutomaticProcessingHistoryStore
{
    private const string CreateSchemaSql = """
        CREATE TABLE IF NOT EXISTS AutomaticProcessingHistory
        (
            AttemptId TEXT NOT NULL PRIMARY KEY,
            Tenam TEXT NOT NULL,
            StartedAtUtcUnixMs INTEGER NOT NULL,
            CompletedAtUtcUnixMs INTEGER NOT NULL,
            Outcome INTEGER NOT NULL CHECK (Outcome BETWEEN 0 AND 2)
        );

        CREATE INDEX IF NOT EXISTS IX_AutomaticProcessingHistory_CompletedAtUtcUnixMs
            ON AutomaticProcessingHistory (CompletedAtUtcUnixMs, Outcome);
        """;

    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _isInitialized;

    public SqliteAutomaticProcessingHistoryStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (!Path.IsPathFullyQualified(databasePath))
        {
            throw new ArgumentException("SQLite database path must be absolute.", nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task<bool> TryAppendAsync(
        AutomaticProcessingAttempt attempt,
        CancellationToken cancellationToken)
    {
        ValidateAttempt(attempt);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO AutomaticProcessingHistory
                (AttemptId, Tenam, StartedAtUtcUnixMs, CompletedAtUtcUnixMs, Outcome)
            VALUES
                ($attemptId, $tenam, $startedAtUtcUnixMs, $completedAtUtcUnixMs, $outcome);
            """;
        command.Parameters.AddWithValue("$attemptId", attempt.AttemptId.ToString("D"));
        command.Parameters.AddWithValue("$tenam", attempt.Tenam);
        command.Parameters.AddWithValue("$startedAtUtcUnixMs", attempt.StartedAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$completedAtUtcUnixMs", attempt.CompletedAtUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$outcome", (int)attempt.Outcome);

        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affectedRows == 1;
    }

    public async Task<AutomaticProcessingHistoryAggregate> GetAggregateAsync(
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        CancellationToken cancellationToken)
    {
        if (fromInclusiveUtc >= toExclusiveUtc)
        {
            throw new ArgumentException("The aggregate interval must have a positive duration.");
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(*),
                COALESCE(SUM(CASE WHEN Outcome = 0 THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN Outcome = 2 THEN 1 ELSE 0 END), 0),
                MIN(CompletedAtUtcUnixMs),
                MAX(CompletedAtUtcUnixMs)
            FROM AutomaticProcessingHistory
            WHERE CompletedAtUtcUnixMs >= $fromInclusiveUtcUnixMs
              AND CompletedAtUtcUnixMs < $toExclusiveUtcUnixMs;
            """;
        command.Parameters.AddWithValue("$fromInclusiveUtcUnixMs", fromInclusiveUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$toExclusiveUtcUnixMs", toExclusiveUtc.ToUnixTimeMilliseconds());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        bool hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!hasRow)
        {
            return new AutomaticProcessingHistoryAggregate(0, 0, 0, null, null);
        }

        long completedCount = reader.GetInt64(0);
        long successCount = reader.GetInt64(1);
        long errorCount = reader.GetInt64(2);

        DateTimeOffset? firstCompletedAtUtc = reader.IsDBNull(3)
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3));
        DateTimeOffset? lastCompletedAtUtc = reader.IsDBNull(4)
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4));

        return new AutomaticProcessingHistoryAggregate(
            completedCount,
            successCount,
            errorCount,
            firstCompletedAtUtc,
            lastCompletedAtUtc);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            string? directoryPath = Path.GetDirectoryName(_databasePath);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new InvalidOperationException("SQLite database directory could not be determined.");
            }

            Directory.CreateDirectory(directoryPath);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = CreateSchemaSql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _isInitialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static void ValidateAttempt(AutomaticProcessingAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (attempt.AttemptId == Guid.Empty)
        {
            throw new ArgumentException("Attempt identifier must not be empty.", nameof(attempt));
        }

        if (string.IsNullOrWhiteSpace(attempt.Tenam))
        {
            throw new ArgumentException("TENAM must not be empty.", nameof(attempt));
        }

        if (attempt.CompletedAtUtc < attempt.StartedAtUtc)
        {
            throw new ArgumentException("Completion time must not precede start time.", nameof(attempt));
        }

        if (!Enum.IsDefined(attempt.Outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "Automatic processing outcome is not supported.");
        }
    }
}
