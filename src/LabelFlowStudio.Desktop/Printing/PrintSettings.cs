using LabelFlowStudio.Application.BoxProcessing;
using System.Text.Json.Serialization;

namespace LabelFlowStudio.Desktop.Printing;

public sealed class PrintSettings
{
    public bool PrintEndLabelEnabled { get; set; } = true;
    public bool PrintStuffingSheetEnabled { get; set; } = true;

    public string EndLabelPrinterName { get; set; } = string.Empty;
    public string StuffingSheetPrinterName { get; set; } = string.Empty;

    public int EndLabelCopies { get; set; } = 2;
    public int StuffingSheetCopies { get; set; } = 1;
    public bool UseScales { get; set; } = true;
    public bool ManualScanAutoPrintEndLabelEnabled { get; set; }

    public WorkMode WorkMode { get; set; } = WorkMode.Manual;

    [JsonIgnore]
    public bool IsComplete =>
        (!PrintEndLabelEnabled || !string.IsNullOrWhiteSpace(EndLabelPrinterName)) &&
        (!PrintStuffingSheetEnabled || !string.IsNullOrWhiteSpace(StuffingSheetPrinterName));
}
