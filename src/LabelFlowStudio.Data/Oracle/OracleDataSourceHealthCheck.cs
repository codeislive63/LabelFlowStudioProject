using LabelFlowStudio.Core.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LabelFlowStudio.Data.Oracle;

/// <summary>
/// Выполняет лёгкую проверку доступности Oracle без бизнес-запроса по TENAM.
/// </summary>
public sealed class OracleDataSourceHealthCheck(IDbContextFactory<LabelDbContext> dbContextFactory) : IDataSourceHealthCheck
{
    private readonly IDbContextFactory<LabelDbContext> _dbContextFactory = dbContextFactory 
        ?? throw new ArgumentNullException(nameof(dbContextFactory));

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Database.CanConnectAsync(cancellationToken);
    }
}
