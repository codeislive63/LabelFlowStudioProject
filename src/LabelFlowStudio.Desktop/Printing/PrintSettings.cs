using System.Text.Json.Serialization;

namespace LabelFlowStudio.Desktop.Printing;

public sealed class PrintSettings
{
    public string EndLabelPrinterName { get; set; } = string.Empty;
    public string StuffingSheetPrinterName { get; set; } = string.Empty;

    public int EndLabelCopies { get; set; } = 2;
    public int StuffingSheetCopies { get; set; } = 1;

    [JsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(EndLabelPrinterName) &&
        !string.IsNullOrWhiteSpace(StuffingSheetPrinterName);
}
