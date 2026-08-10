using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Data.Oracle;
using LabelFlowStudio.Data.Oracle.Repositories;
using LabelFlowStudio.Data.Statistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.Data;

public static class DataServiceCollectionExtensions
{
    private const string OracleConnectionStringName = "Oracle";

    public static IServiceCollection AddLabelFlowDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString(OracleConnectionStringName);
        
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Oracle connection string is not configured. Add it to user-secrets as ConnectionStrings:Oracle."
            );
        }

        services.AddDbContextFactory<LabelDbContext>(optionsBuilder =>
        {
            optionsBuilder.UseOracle(connectionString);
        });

        services.AddSingleton<ILabelRepository, LabelRepository>();
        services.AddSingleton<IDataSourceHealthCheck, OracleDataSourceHealthCheck>();
        services.AddSingleton<IAutomaticProcessingHistoryStore>(_ =>
            new SqliteAutomaticProcessingHistoryStore(
                AutomaticProcessingDatabasePathResolver.Resolve(configuration)));

        return services;
    }
}
