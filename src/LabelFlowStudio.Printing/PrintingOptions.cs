using System.ComponentModel.DataAnnotations;

namespace LabelFlowStudio.Printing;

public sealed class PrintingOptions
{
    public string? DropSheetPrinterName { get; set; }

    public string? EndLabelPrinterName { get; set; }

    public bool ShowDialogForDropSheet { get; set; } = false;

    public bool ShowDialogForEndLabel { get; set; } = true;

    [Range(20, 300)]
    public double EndLabelWidthMm { get; set; } = 100;

    [Range(20, 300)]
    public double EndLabelHeightMm { get; set; } = 150;
}