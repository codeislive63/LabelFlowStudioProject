using LabelFlowStudio.App.BoxProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddLabelFlowApplication(this IServiceCollection services)
    {
        services.AddSingleton<IBoxProcessingService, BoxProcessingService>();
        return services;
    }
}
