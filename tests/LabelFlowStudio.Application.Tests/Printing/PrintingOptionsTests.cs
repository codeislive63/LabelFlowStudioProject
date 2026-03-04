using LabelFlowStudio.Printing;

namespace LabelFlowStudio.Application.Tests.Printing;

public sealed class PrintingOptionsTests
{
    [Fact]
    public void Defaults_AreExpected()
    {
        var options = new PrintingOptions();

        Assert.False(options.ShowDialogForDropSheet);
        Assert.True(options.ShowDialogForEndLabel);
        Assert.Equal(100, options.EndLabelWidthMm);
        Assert.Equal(150, options.EndLabelHeightMm);
        Assert.Null(options.DropSheetPrinterName);
        Assert.Null(options.EndLabelPrinterName);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new PrintingOptions
        {
            DropSheetPrinterName = "DropPrinter",
            EndLabelPrinterName = "EndPrinter",
            ShowDialogForDropSheet = true,
            ShowDialogForEndLabel = false,
            EndLabelWidthMm = 200,
            EndLabelHeightMm = 220
        };

        Assert.Equal("DropPrinter", options.DropSheetPrinterName);
        Assert.Equal("EndPrinter", options.EndLabelPrinterName);
        Assert.True(options.ShowDialogForDropSheet);
        Assert.False(options.ShowDialogForEndLabel);
        Assert.Equal(200, options.EndLabelWidthMm);
        Assert.Equal(220, options.EndLabelHeightMm);
    }
}
