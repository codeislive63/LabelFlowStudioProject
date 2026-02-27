using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.BoxProcessing;

/// <summary>
/// Содержит результат обработки короба и параметры печати
/// </summary>
public sealed record BoxProcessingResponse(
    BoxProcessingStatus Status,
    string Message,
    IReadOnlyList<LabelRecord> Records,
    decimal? Weight,
    bool ShouldPrintDropSheet,
    bool ShouldPrintEmptyDropSheet,
    bool ShouldPrintEndLabels
);
