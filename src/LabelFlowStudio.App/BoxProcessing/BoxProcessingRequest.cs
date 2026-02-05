namespace LabelFlowStudio.App.BoxProcessing;

public sealed record BoxProcessingRequest(
    string Tenam,
    WorkMode Mode,
    bool ShouldPrintEndLabels
);
