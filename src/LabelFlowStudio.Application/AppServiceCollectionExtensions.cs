using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Policies;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.Application;

/// <summary>
/// Регистрирует сервисы прикладного слоя
/// </summary>
public static class AppServiceCollectionExtensions
{
    /// <summary>
    /// Добавляет сервисы прикладного слоя LabelFlowStudio
    /// </summary>
    public static IServiceCollection AddLabelFlowApplication(this IServiceCollection services)
    {
        services.AddSingleton<IBoxWeightResolver, BoxWeightResolver>();
        services.AddSingleton<IBoxProcessingPolicy, BoxProcessingPolicy>();
        services.AddSingleton<IBoxProcessingService, BoxProcessingService>();
        services.AddSingleton<IBoxWeightService, BoxWeightService>();

        return services;
    }
}
