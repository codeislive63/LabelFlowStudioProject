namespace LabelFlowStudio.Application.BoxProcessing;

/// <summary>
/// Сервис, отвечающий за обработку коробок в рамках бизнес-процесса
/// </summary>
public interface IBoxProcessingService
{
    /// <summary>
    /// Обрабатывает запрос по коробу и возвращает итоговый статус
    /// </summary>
    Task<BoxProcessingResponse> ProcessAsync(BoxProcessingRequest request, CancellationToken cancellationToken);
}
