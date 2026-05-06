using LabelFlowStudio.Application.BoxProcessing.Contracts;

namespace LabelFlowStudio.Application.BoxProcessing.Weight;

/// <summary>
/// Сервис, отвечающий за сохранение веса короба
/// </summary>
public interface IBoxWeightService
{
    /// <summary>
    /// Сохраняет введенный вручную вес короба в базе данных
    /// </summary>
    Task<BoxWeightUpdateResult> UpdateWeightAsync(
        string tenam,
        decimal weight,
        CancellationToken cancellationToken);
}
