using LabelFlowStudio.Application.BoxProcessing.Contracts;

namespace LabelFlowStudio.Application.BoxProcessing;

/// <summary>
/// Сервис, отвечающий за обработку коробов в рамках бизнес-процесса
/// </summary>
public interface IBoxProcessingService
{
    /// <summary>
    /// Обрабатывает запрос по коробу и возвращает итоговый статус
    /// </summary>
    Task<BoxProcessingResponse> ProcessAsync(
        BoxProcessingRequest request,
        CancellationToken cancellationToken);
}
