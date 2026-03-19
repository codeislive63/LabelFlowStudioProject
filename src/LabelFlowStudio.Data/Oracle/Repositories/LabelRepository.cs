using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace LabelFlowStudio.Data.Oracle.Repositories;

/// <summary>
/// Репозиторий чтения данных по коробам из Oracle
/// </summary>
public sealed class LabelRepository : ILabelRepository
{
    private readonly IDbContextFactory<LabelDbContext> _dbContextFactory;
    private readonly ILogger<LabelRepository> _logger;

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

        await using LabelDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.LabelRecords
                              .AsNoTracking()
                              .Where(record => record.Tenam == normalizedTenam)
                              .ToListAsync(cancellationToken);
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
}
