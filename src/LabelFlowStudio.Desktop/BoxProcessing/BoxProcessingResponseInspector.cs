using LabelFlowStudio.Application.BoxProcessing;

namespace LabelFlowStudio.Desktop.BoxProcessing;

/// <summary>
/// Утилита для проверки содержимого ответа обработки короба
/// </summary>
internal static class BoxProcessingResponseInspector
{
    /// <summary>
    /// Определяет наличие валидного веса в ответе
    /// </summary>
    public static bool HasWeight(BoxProcessingResponse response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        if (response.Weight.HasValue && response.Weight.Value > 0)
        {
            return true;
        }

        if (response.Records.Count == 0)
        {
            return false;
        }

        var brutto = response.Records[0].Brutto;
        return brutto.HasValue && brutto.Value > 0;
    }
}
