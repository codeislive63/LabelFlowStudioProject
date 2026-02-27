using LabelFlowStudio.Desktop.Printing;

namespace LabelFlowStudio.Application.Tests.Printing;

public sealed class PrintSettingsTests
{
    [Fact]
    public void IsComplete_WhenPrintersEnabledAndNamesFilled_ReturnsTrue()
    {
        var settings = new PrintSettings
        {
            PrintEndLabelEnabled = true,
            EndLabelPrinterName = "Zebra",
            PrintStuffingSheetEnabled = true,
            StuffingSheetPrinterName = "HP"
        };

        Assert.True(settings.IsComplete);
    }

    [Fact]
    public void IsComplete_WhenEnabledPrinterNameMissing_ReturnsFalse()
    {
        var settings = new PrintSettings
        {
            PrintEndLabelEnabled = true,
            EndLabelPrinterName = "",
            PrintStuffingSheetEnabled = false
        };

        Assert.False(settings.IsComplete);
    }

    [Fact]
    public void IsComplete_WhenFeatureDisabled_IgnoresMissingPrinter()
    {
        var settings = new PrintSettings
        {
            PrintEndLabelEnabled = false,
            EndLabelPrinterName = "",
            PrintStuffingSheetEnabled = true,
            StuffingSheetPrinterName = "LaserJet"
        };

        Assert.True(settings.IsComplete);
    }
}