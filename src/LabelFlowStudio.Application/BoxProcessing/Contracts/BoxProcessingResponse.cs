using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.BoxProcessing.Contracts;

/// <summary>
/// Содержит результат обработки короба и план дальнейшей печати
/// </summary>
public sealed record BoxProcessingResponse(
    BoxProcessingStatus Status,
    string Message,
    IReadOnlyList<LabelRecord> Records,
    decimal? Weight,
    PrintPlan PrintPlan
);
