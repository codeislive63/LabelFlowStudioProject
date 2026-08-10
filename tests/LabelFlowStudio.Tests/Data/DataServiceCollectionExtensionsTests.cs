using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Data;
using LabelFlowStudio.Data.Oracle;
using LabelFlowStudio.Data.Oracle.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.Application.Tests.Data;

public sealed class DataServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLabelFlowDataAccess_Throws_WhenConnectionStringMissing()
    {
        var configuration = new ConfigurationBuilder()
                                .AddInMemoryCollection()
                                .Build();

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddLabelFlowDataAccess(configuration);
        });

        Assert.Contains("Oracle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddLabelFlowDataAccess_RegistersRepositoryAndDbFactory()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Oracle"] = "Data Source=localhost/XEPDB1;User Id=u;Password=p;"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();

        services.AddLabelFlowDataAccess(configuration);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IDbContextFactory<LabelDbContext>));

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(ILabelRepository)
                && descriptor.ImplementationType == typeof(LabelRepository)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IAutomaticProcessingHistoryStore)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
    }
}
