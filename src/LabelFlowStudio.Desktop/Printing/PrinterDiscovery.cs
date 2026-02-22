using System.Drawing.Printing;

namespace LabelFlowStudio.Desktop.Printing;

public static class PrinterDiscovery
{
    public static IReadOnlyList<string> GetInstalledPrinters()
    {
        return PrinterSettings.InstalledPrinters
            .Cast<string>()
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .OrderBy(p => p)
            .ToList();
    }

    public static bool IsPrinterInstalled(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return false;
        }

        return PrinterSettings.InstalledPrinters.Cast<string>().Any(p => p == printerName);
    }
}
