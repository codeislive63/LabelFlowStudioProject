using System.Windows;

namespace LabelFlowStudio.Desktop;

/// <summary>
/// Результат быстрой печати торцевой этикетки
/// </summary>
public enum EndLabelQuickPrintResult
{
    Printed,
    Failed
}

/// <summary>
/// Быстрая печать торцевой этикетки через скрытый WebView2
/// </summary>
public static class EndLabelQuickPrinter
{
    /// <summary>
    /// Печатает HTML торцевой этикетки без пользовательского диалога
    /// </summary>
    public static async Task<EndLabelQuickPrintResult> PrintHtmlAsync(
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

        return isPrinted ? EndLabelQuickPrintResult.Printed : EndLabelQuickPrintResult.Failed;
    }
}
