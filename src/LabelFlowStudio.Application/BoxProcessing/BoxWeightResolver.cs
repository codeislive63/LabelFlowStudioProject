using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.BoxProcessing;

/// <summary>
/// Определяет вес короба по строкам, полученным из базы данных
/// </summary>
public sealed class BoxWeightResolver : IBoxWeightResolver
{
    private const string WeightMissingMessage = "Нет веса в БД. Поставьте короб на весы";
    private const string WeightConflictMessage = "В БД найдено несколько разных значений веса для одного короба";

    /// <summary>
    /// Возвращает итоговый вес короба или причину невозможности его определить
    /// </summary>
    public BoxWeightResolution Resolve(IReadOnlyList<LabelRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var weights = records
            .Select(record => record.Brutto)
            .Where(weight => weight.HasValue && weight.Value > 0)
            .Select(weight => weight!.Value)
            .Distinct()
            .ToArray();

        return weights.Length switch
        {
            0 => BoxWeightResolution.Missing(WeightMissingMessage),
            1 => BoxWeightResolution.Found(weights[0]),
            _ => BoxWeightResolution.Conflict(WeightConflictMessage)
        };
    }
}
