using System.Windows;

namespace LabelFlowStudio.Desktop;

/// <summary>
/// Результат быстрой печати листа сброса
/// </summary>
public enum StuffingSheetQuickPrintResult
{
    Printed,
    Failed
}

/// <summary>
/// Быстрая печать листа сброса через скрытый WebView2
/// </summary>
public static class StuffingSheetQuickPrinter
{
    /// <summary>
    /// Печатает HTML листа сброса без пользовательского диалога
    /// </summary>
    public static async Task<StuffingSheetQuickPrintResult> PrintHtmlAsync(
        string html,
        string printerName,
        int copies,
        Window? owner,
        CancellationToken cancellationToken)
    {
        var isPrinted = await SilentHtmlPrinter.PrintHtmlAsync(
            html,
            printerName,
            copies,
            owner,
            cancellationToken);

        return isPrinted ? StuffingSheetQuickPrintResult.Printed : StuffingSheetQuickPrintResult.Failed;
    }
}
