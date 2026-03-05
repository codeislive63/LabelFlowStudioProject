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

    /// <summary>
    /// Сохраняет введенный вручную вес короба в базе данных
    /// </summary>
    Task<bool> UpdateWeightAsync(string tenam, decimal weight, CancellationToken cancellationToken);
}
