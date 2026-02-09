using LabelFlowStudio.Data;
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
}
