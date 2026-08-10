using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.ViewModels;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class PrintSettingsEditorViewModelTests
{
    private static readonly string[] InstalledPrinters = ["End printer", "Sheet printer"];

    [Fact]
    public void EditingDraft_DoesNotMutateActiveSettings()
    {
        var active = CreateValidSettings();
        var editor = CreateEditor(active);

        editor.PrintEndLabelEnabled = false;
        editor.EndLabelPrinterName = "Sheet printer";
        editor.EndLabelCopies = 12;
        editor.UseScales = false;

        Assert.True(active.PrintEndLabelEnabled);
        Assert.Equal("End printer", active.EndLabelPrinterName);
        Assert.Equal(2, active.EndLabelCopies);
        Assert.True(active.UseScales);
    }

    [Fact]
    public void EnabledPrintRoleWithoutPrinter_IsInvalid()
    {
        var active = CreateValidSettings();
        active.EndLabelPrinterName = string.Empty;

        var editor = CreateEditor(active);

        Assert.False(editor.IsValid);
        Assert.Contains("Выберите принтер", editor.EndLabelPrinterValidationMessage);
    }

    [Fact]
    public void SelectedPrinterMissingFromWindowsCatalog_IsInvalid()
    {
        var active = CreateValidSettings();
        active.EndLabelPrinterName = "Removed printer";

        var editor = CreateEditor(active);

        Assert.False(editor.IsValid);
        Assert.True(editor.IsEndLabelPrinterMissing);
        Assert.Equal("Принтер не найден", editor.EndLabelPrinterStateText);
        Assert.Contains("не найден в Windows", editor.EndLabelPrinterValidationMessage);
    }

    [Fact]
    public void DisabledPrintRole_IsNeutralAndDoesNotRequirePrinter()
    {
        var active = CreateValidSettings();
        active.PrintEndLabelEnabled = false;
        active.EndLabelPrinterName = "Removed printer";

        var editor = CreateEditor(active);

        Assert.True(editor.IsValid);
        Assert.Equal("Отключено", editor.EndLabelPrinterStateText);
        Assert.False(editor.IsEndLabelPrinterInstalled);
        Assert.False(editor.IsEndLabelPrinterMissing);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public void Copies_InsideInclusiveRange_AreValid(int copies)
    {
        var editor = CreateEditor(CreateValidSettings());

        editor.EndLabelCopies = copies;
        editor.StuffingSheetCopies = copies;

        Assert.True(editor.IsValid);
        Assert.Empty(editor.EndLabelCopiesValidationMessage);
        Assert.Empty(editor.StuffingSheetCopiesValidationMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void Copies_OutsideInclusiveRange_AreInvalid(int copies)
    {
        var editor = CreateEditor(CreateValidSettings());

        editor.EndLabelCopies = copies;

        Assert.False(editor.IsValid);
        Assert.Contains("от 1 до 99", editor.EndLabelCopiesValidationMessage);
    }

    [Fact]
    public void MergeWithLatestActive_AppliesDraftWorkModeAndPreservesManualAutoprint()
    {
        var opened = CreateValidSettings();
        opened.WorkMode = WorkMode.Manual;
        opened.ManualScanAutoPrintEndLabelEnabled = false;
        var editor = CreateEditor(opened);
        editor.EndLabelCopies = 8;

        var latest = CreateValidSettings();
        latest.WorkMode = WorkMode.Automatic;
        latest.ManualScanAutoPrintEndLabelEnabled = true;

        var merged = editor.MergeWithLatestActive(latest);

        Assert.Equal(8, merged.EndLabelCopies);
        Assert.Equal(WorkMode.Manual, merged.WorkMode);
        Assert.True(merged.ManualScanAutoPrintEndLabelEnabled);
        Assert.NotSame(latest, merged);
    }

    private static PrintSettingsEditorViewModel CreateEditor(PrintSettings active) =>
        new(active, InstalledPrinters, new PrintSettingsValidator());

    private static PrintSettings CreateValidSettings() => new()
    {
        PrintEndLabelEnabled = true,
        EndLabelPrinterName = "End printer",
        EndLabelCopies = 2,
        PrintStuffingSheetEnabled = true,
        StuffingSheetPrinterName = "Sheet printer",
        StuffingSheetCopies = 1,
        UseScales = true,
        WorkMode = WorkMode.Manual
    };
}
