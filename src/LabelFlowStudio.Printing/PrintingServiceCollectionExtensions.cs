using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.Printing;

public static class PrintingServiceCollectionExtensions
{
    private const string PrintingSection = "Printing";

    public static IServiceCollection AddLabelFlowPrinting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PrintingOptions>()
                .Bind(configuration.GetSection(PrintingSection))
                .ValidateDataAnnotations();

        services.AddSingleton<EndLabelDocumentBuilder>();
        services.AddSingleton<IPrintService, WpfPrintService>();
        return services;
    }
}
