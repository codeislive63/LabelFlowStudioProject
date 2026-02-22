using Microsoft.Web.WebView2.Wpf;
using System.Drawing.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace LabelFlowStudio.Desktop;

public enum StuffingSheetQuickPrintResult
{
    Printed,
    Failed
}

public static class StuffingSheetQuickPrinter
{
    public static async Task<StuffingSheetQuickPrintResult> PrintHtmlAsync(
        string html,
        string printerName,
        int copies,
        Window? owner,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return StuffingSheetQuickPrintResult.Failed;
        }

        if (string.IsNullOrWhiteSpace(printerName))
        {
            return StuffingSheetQuickPrintResult.Failed;
        }

        if (!IsPrinterInstalled(printerName))
        {
            return StuffingSheetQuickPrintResult.Failed;
        }

        if (copies <= 0)
        {
            copies = 1;
        }

        WebView2PrintHostWindow? hostWindow = null;

        try
        {
            hostWindow = new WebView2PrintHostWindow
            {
                Owner = owner
            };

            hostWindow.Show();

            await hostWindow.EnsureInitializedAsync(cancellationToken);
            await hostWindow.NavigateToStringAsync(html, cancellationToken);

            var printed = await hostWindow.TryPrintToPrinterAsync(printerName, copies, cancellationToken);
            return printed ? StuffingSheetQuickPrintResult.Printed : StuffingSheetQuickPrintResult.Failed;
        }
        catch
        {
            return StuffingSheetQuickPrintResult.Failed;
        }
        finally
        {
            try
            {
                hostWindow?.Close();
            }
            catch
            {
            }
        }
    }

    private static bool IsPrinterInstalled(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return false;
        }

        foreach (string printer in PrinterSettings.InstalledPrinters)
        {
            if (printer == printerName)
            {
                return true;
            }
        }

        return false;
    }
}
