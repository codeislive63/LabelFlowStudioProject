using LabelFlowStudio.Application.BoxProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.Application.Tests;

public sealed class AppServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLabelFlowApplication_RegistersBoxProcessingServiceAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddLabelFlowApplication();

        var descriptor = Assert.Single(
            services,
            registration => registration.ServiceType == typeof(IBoxProcessingService)
        );

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(BoxProcessingService), descriptor.ImplementationType);
    }
}