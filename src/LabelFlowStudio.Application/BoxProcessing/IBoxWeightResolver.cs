using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.BoxProcessing;

/// <summary>
/// Определяет вес короба по найденным строкам данных
/// </summary>
public interface IBoxWeightResolver
{
    /// <summary>
    /// Возвращает итоговый вес короба или причину невозможности его определить
    /// </summary>
    BoxWeightResolution Resolve(IReadOnlyList<LabelRecord> records);
}
