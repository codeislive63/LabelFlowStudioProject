using LabelFlowStudio.Application.BoxProcessing.Contracts;

namespace LabelFlowStudio.Desktop.BoxProcessing;

/// <summary>
/// Утилита для проверки содержимого ответа обработки короба
/// </summary>
public static class BoxProcessingResponseInspector
{
    /// <summary>
    /// Определяет наличие валидного веса в ответе
    /// </summary>
    public static bool HasWeight(BoxProcessingResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Weight.HasValue && response.Weight.Value > 0)
        {
            return true;
        }

        if (response.Records.Count == 0)
        {
            return false;
        }

        var weights = response.Records
            .Select(record => record.Brutto)
            .Where(weight => weight.HasValue && weight.Value > 0)
            .Select(weight => weight!.Value)
            .Distinct()
            .ToArray();

        return weights.Length == 1;
    }
}
