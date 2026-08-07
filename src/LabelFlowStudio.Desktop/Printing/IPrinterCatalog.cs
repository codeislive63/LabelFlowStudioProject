namespace LabelFlowStudio.Desktop.Printing;

public interface IPrinterCatalog
{
    IReadOnlyList<string> GetInstalledPrinters();
}

/// <summary>
/// Reads Windows printers without allowing a discovery failure to break Settings.
/// An empty catalog is intentionally reported as unavailable, never as ready.
/// </summary>
public sealed class WindowsPrinterCatalog : IPrinterCatalog
{
    public IReadOnlyList<string> GetInstalledPrinters()
    {
        try
        {
            return PrinterDiscovery.GetInstalledPrinters();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
