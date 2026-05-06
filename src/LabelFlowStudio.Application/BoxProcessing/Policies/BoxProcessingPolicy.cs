using LabelFlowStudio.Application.BoxProcessing.Contracts;

namespace LabelFlowStudio.Application.BoxProcessing.Policies;

/// <summary>
/// Реализует правила обработки короба для ручного и автоматического режима
/// </summary>
public sealed class BoxProcessingPolicy : IBoxProcessingPolicy
{
    /// <summary>
    /// Формирует план печати для успешной обработки
    /// </summary>
    public PrintPlan CreateSuccessPrintPlan(BoxProcessingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PrintPlan(
            PrintDropSheet: request.ShouldPrintStuffingSheet,
            PrintEmptyDropSheet: false,
            PrintEndLabels: request.ShouldPrintEndLabels
        );
    }

    /// <summary>
    /// Формирует план печати для случая, когда вес не найден
    /// </summary>
    public PrintPlan CreateMissingWeightPrintPlan(BoxProcessingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Если короб идет в автоматическом режиме без весов,
        // печатаем только пустой лист сброса, чтобы не останавливать поток.
        if (request.Mode == WorkMode.Automatic && !request.UseScales)
        {
            return new PrintPlan(
                PrintDropSheet: false,
                PrintEmptyDropSheet: request.ShouldPrintStuffingSheet,
                PrintEndLabels: false
            );
        }

        return PrintPlan.None;
    }
}
