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
            .Validate(
                options => !options.IsEnabled || !string.IsNullOrWhiteSpace(options.PortName),
                "Devices:Scanner:PortName is required when Devices:Scanner:IsEnabled is true"
            );

        var isScannerEnabled = configuration.GetValue<bool>($"{ScannerSection}:Enabled");

        if (isScannerEnabled)
        {
            services.AddSingleton<IBoxScanner, ComPortBoxScanner>();
        }
        else
        {
            services.AddSingleton<IBoxScanner, NullBoxScanner>();
        }

        return services;
    }
}
