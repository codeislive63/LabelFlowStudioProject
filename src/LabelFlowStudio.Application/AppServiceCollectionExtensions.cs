using LabelFlowStudio.Application.BoxProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.Application;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddLabelFlowApplication(this IServiceCollection services)
    {
        services.AddSingleton<IBoxProcessingService, BoxProcessingService>();
        return services;
    }
}
