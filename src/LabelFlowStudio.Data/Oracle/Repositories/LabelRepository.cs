using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelFlowStudio.Data.Oracle.Repositories;

/// <summary>
/// Репозиторий чтения данных по коробам из Oracle
/// </summary>
public sealed class LabelRepository : ILabelRepository
{
    private readonly IDbContextFactory<LabelDbContext> _dbContextFactory;

    /// <summary>
    /// Создает репозиторий с фабрикой контекста базы данных
    /// </summary>
    public LabelRepository(IDbContextFactory<LabelDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
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
}
