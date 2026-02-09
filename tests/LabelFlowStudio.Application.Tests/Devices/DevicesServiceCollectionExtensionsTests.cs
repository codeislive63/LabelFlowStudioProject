using LabelFlowStudio.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.Application.Tests.Devices;

public sealed class DevicesServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLabelFlowDevices_RegistersScanner()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Devices:Scanner:PortName"] = "COM1",
                ["Devices:Scanner:LineSeparator"] = "\n"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddLabelFlowDevices(config);

        var provider = services.BuildServiceProvider();

        var scanner = provider.GetService<LabelFlowStudio.Devices.BoxScanner.IBoxScanner>();

        Assert.NotNull(scanner);
    }
}
