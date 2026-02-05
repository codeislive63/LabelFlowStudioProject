using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.App.BoxProcessing;

public sealed record BoxProcessingResponse(
    BoxProcessingStatus Status,
    string Message,
    IReadOnlyList<LabelRecord> Records,
    decimal? Weight,
    bool ShouldPrintDropSheet,
    bool ShouldPrintEmptyDropSheet,
    bool ShouldPrintEndLabels
);
