namespace LabelFlowStudio.Application.BoxProcessing;

public sealed record BoxProcessingRequest(
    string Tenam,
    WorkMode Mode,
    bool ShouldPrintEndLabels,
    bool ShouldPrintStuffingSheet
);
