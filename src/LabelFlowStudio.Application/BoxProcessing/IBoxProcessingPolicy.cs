namespace LabelFlowStudio.Application.BoxProcessing;

/// <summary>
/// Определяет правила обработки короба для разных режимов работы
/// </summary>
public interface IBoxProcessingPolicy
{
    /// <summary>
    /// Формирует план печати для успешной обработки
    /// </summary>
    PrintPlan CreateSuccessPrintPlan(BoxProcessingRequest request);

    /// <summary>
    /// Формирует план печати для случая, когда вес не найден
    /// </summary>
    PrintPlan CreateMissingWeightPrintPlan(BoxProcessingRequest request);
}
