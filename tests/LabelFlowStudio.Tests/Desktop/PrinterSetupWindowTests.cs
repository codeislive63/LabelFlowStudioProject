using LabelFlowStudio.Desktop.Printing;

namespace LabelFlowStudio.Application.Tests.Desktop;

public sealed class PrinterSetupWindowTests
{
    [Fact]
    public void ShouldShowSettings_WhenSettingsAreCompleteAndOpenIsNotForced_ReturnsFalse()
    {
        var settings = CreateCompleteSettings();

        var shouldShow = PrinterSetupWindow.ShouldShowSettings(settings, forceOpen: false);

        Assert.False(shouldShow);
    }

    [Fact]
    public void ShouldShowSettings_WhenSettingsAreIncomplete_ReturnsTrue()
    {
        var settings = CreateCompleteSettings();
        settings.EndLabelPrinterName = string.Empty;

        var shouldShow = PrinterSetupWindow.ShouldShowSettings(settings, forceOpen: false);

        Assert.True(shouldShow);
    }

    [Fact]
    public void ShouldShowSettings_WhenSettingsAreCompleteAndOpenIsForced_ReturnsTrue()
    {
        var settings = CreateCompleteSettings();

        var shouldShow = PrinterSetupWindow.ShouldShowSettings(settings, forceOpen: true);

        Assert.True(shouldShow);
    }

    [Fact]
    public void ShouldShowSettings_WhenPrinterRoleIsDisabled_DoesNotRequireItsPrinterName()
    {
        var settings = CreateCompleteSettings();
        settings.PrintEndLabelEnabled = false;
        settings.EndLabelPrinterName = string.Empty;

        var shouldShow = PrinterSetupWindow.ShouldShowSettings(settings, forceOpen: false);

        Assert.False(shouldShow);
    }

    [Fact]
    public void CreateSettingsDraft_CopiesAllValuesWithoutSharingMutableState()
    {
        var settings = CreateCompleteSettings();
        settings.EndLabelCopies = 3;
        settings.StuffingSheetCopies = 2;
        settings.UseScales = false;
        settings.ManualScanAutoPrintEndLabelEnabled = true;
        settings.WorkMode = LabelFlowStudio.Application.BoxProcessing.Contracts.WorkMode.Automatic;

        var draft = PrinterSetupWindow.CreateSettingsDraft(settings);

        Assert.NotSame(settings, draft);
        Assert.Equivalent(settings, draft, strict: true);

        draft.EndLabelPrinterName = "Changed only in dialog";
        draft.PrintStuffingSheetEnabled = false;

        Assert.Equal("End-label printer", settings.EndLabelPrinterName);
        Assert.True(settings.PrintStuffingSheetEnabled);
    }

    private static PrintSettings CreateCompleteSettings()
    {
        return new PrintSettings
        {
            PrintEndLabelEnabled = true,
            EndLabelPrinterName = "End-label printer",
            PrintStuffingSheetEnabled = true,
            StuffingSheetPrinterName = "Stuffing-sheet printer"
        };
    }
}
