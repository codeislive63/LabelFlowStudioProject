namespace LabelFlowStudio.Application.BoxProcessing.Contracts;

/// <summary>
/// Описывает входные параметры обработки короба
/// </summary>
public sealed record BoxProcessingRequest(
    string Tenam,
    WorkMode Mode,
    bool ShouldPrintEndLabels,
    bool ShouldPrintStuffingSheet,
    bool UseScales
);
