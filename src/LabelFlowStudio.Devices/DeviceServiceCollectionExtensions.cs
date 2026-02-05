using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.Devices;

public static class DeviceServiceCollectionExtensions
{
    private const string ScannerSection = "Devices:Scanner";

    public static IServiceCollection AddLabelFlowDevices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BoxScannerOptions>()
                .Bind(configuration.GetSection(ScannerSection))
                .ValidateDataAnnotations()
                .Validate(options => !string.IsNullOrWhiteSpace(options.PortName), "Devices:Scanner:PortName is required");

        services.AddSingleton<IBoxScanner, ComPortBoxScanner>();

        return services;
    }
}
