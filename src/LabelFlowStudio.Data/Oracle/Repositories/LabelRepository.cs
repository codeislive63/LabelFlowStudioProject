using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace LabelFlowStudio.Data.Oracle.Repositories;

/// <summary>
/// Репозиторий чтения данных по коробам из Oracle
/// </summary>
public sealed class LabelRepository : ILabelRepository
{
    private readonly IDbContextFactory<LabelDbContext> _dbContextFactory;
    private readonly ILogger<LabelRepository> _logger;

    private static readonly TimeSpan QueryBurstWindow = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, QueryBurstTracker> QueryBurstTrackers = new(StringComparer.Ordinal);


    /// <summary>
    /// Создает репозиторий с фабрикой контекста базы данных
    /// </summary>
    public LabelRepository(IDbContextFactory<LabelDbContext> dbContextFactory, ILogger<LabelRepository> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Возвращает список записей по TENAM
    /// </summary>
    public async Task<IReadOnlyList<LabelRecord>> GetByTenamAsync(string tenam, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(tenam));
        }

        string normalizedTenam = tenam.Trim();
        var startedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var queryCount = RegisterQueryHit(normalizedTenam, startedAtUtc, out var windowStartedAtUtc);

        _logger.LogInformation(
            "DB query started for TENAM {Tenam}. Hit {QueryCount} within {WindowSeconds}s window (window started at {WindowStartedAtUtc:O}).",
            normalizedTenam,
            queryCount,
            QueryBurstWindow.TotalSeconds,
            windowStartedAtUtc);

        await using LabelDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var records = await dbContext.LabelRecords
            .AsNoTracking()
            .Where(record => record.Tenam == normalizedTenam)
            .ToListAsync(cancellationToken);

        stopwatch.Stop();

        if (queryCount >= 3)
        {
            _logger.LogWarning(
                "Repeated DB reads detected for TENAM {Tenam}: {QueryCount} queries within {WindowSeconds}s. Latest query returned {RecordCount} rows in {ElapsedMs} ms.",
                normalizedTenam,
                queryCount,
                QueryBurstWindow.TotalSeconds,
                records.Count,
                stopwatch.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogInformation(
                "DB query completed for TENAM {Tenam}. Returned {RecordCount} rows in {ElapsedMs} ms.",
                normalizedTenam,
                records.Count,
                stopwatch.ElapsedMilliseconds);
        }

        return records;
    }

    public async Task<bool> UpdateBruttoByTenamAsync(string tenam, decimal brutto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(tenam));
        }

        if (brutto <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(brutto), "Weight must be greater than zero.");
        }

        string normalizedTenam = tenam.Trim();
        decimal bruttoInDatabaseUnits = brutto * 1_000_000m;

        await using LabelDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            var affectedRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE TE_T@WMS SET TEGEWBRU = {bruttoInDatabaseUnits} WHERE TENAM = {normalizedTenam}",
                cancellationToken
            );

            _logger.LogInformation(
                "Updated TEGEWBRU in TE_T@WMS for TENAM {Tenam}. Brutto: {Brutto}, Database value: {DatabaseValue}, Affected rows: {AffectedRows}",
                normalizedTenam,
                brutto,
                bruttoInDatabaseUnits,
                affectedRows
            );

            return affectedRows > 0;
        }
        catch (OracleException exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to update TE_T@WMS for TENAM {Tenam}. Brutto: {Brutto}, Database value: {DatabaseValue}",
                normalizedTenam,
                brutto,
                bruttoInDatabaseUnits
            );

            return false;
        }
    }


    private static int RegisterQueryHit(string tenam, DateTime startedAtUtc, out DateTime windowStartedAtUtc)
    {
        var tracker = QueryBurstTrackers.AddOrUpdate(
            tenam,
            _ => new QueryBurstTracker(startedAtUtc, 1),
            (_, current) => current.TryRegister(startedAtUtc, QueryBurstWindow));

        windowStartedAtUtc = tracker.WindowStartedAtUtc;
        return tracker.Count;
    }

    private sealed record QueryBurstTracker(DateTime WindowStartedAtUtc, int Count)
    {
        public QueryBurstTracker TryRegister(DateTime startedAtUtc, TimeSpan window)
        {
            if ((startedAtUtc - WindowStartedAtUtc) > window)
            {
                return this with
                {
                    WindowStartedAtUtc = startedAtUtc,
                    Count = 1
                };
            }

            return this with { Count = Count + 1 };
        }
    }

}