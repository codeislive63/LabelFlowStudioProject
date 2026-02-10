using LabelFlowStudio.Devices;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.Application.Tests.Devices;

public sealed class DevicesServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLabelFlowDevices_WhenDisabled_RegistersNullScanner()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddLabelFlowDevices(config);

        var provider = services.BuildServiceProvider();
        var scanner = provider.GetService<IBoxScanner>();

        Assert.NotNull(scanner);
        Assert.IsType<NullBoxScanner>(scanner);
    }

    [Fact]
    public void AddLabelFlowDevices_WhenEnabled_RegistersComPortScanner()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Devices:Scanner:IsEnabled"] = "true",
                ["Devices:Scanner:PortName"] = "COM1",
                ["Devices:Scanner:LineSeparator"] = "\n"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddLabelFlowDevices(config);

        var provider = services.BuildServiceProvider();
        var scanner = provider.GetService<IBoxScanner>();

        Assert.NotNull(scanner);
        Assert.IsType<ComPortBoxScanner>(scanner);
    }
}
