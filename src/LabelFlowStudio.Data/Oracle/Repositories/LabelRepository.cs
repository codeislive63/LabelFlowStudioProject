using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelFlowStudio.Data.Oracle.Repositories;

public sealed class LabelRepository : ILabelRepository
{
    private readonly IDbContextFactory<LabelDbContext> _dbContextFactory;

    public LabelRepository(IDbContextFactory<LabelDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

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
