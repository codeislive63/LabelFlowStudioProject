using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Drawing.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace LabelFlowStudio.Desktop;

public enum EndLabelQuickPrintResult
{
    Printed,
    Failed
}

public static class EndLabelQuickPrinter
{
    public static async Task<EndLabelQuickPrintResult> PrintHtmlAsync(
        string html,
        string printerName,
        int copies,
        Window? owner,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return EndLabelQuickPrintResult.Failed;
        }

        if (string.IsNullOrWhiteSpace(printerName))
        {
            return EndLabelQuickPrintResult.Failed;
        }

        if (!IsPrinterInstalled(printerName))
        {
            return EndLabelQuickPrintResult.Failed;
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
            return printed ? EndLabelQuickPrintResult.Printed : EndLabelQuickPrintResult.Failed;
        }
        catch
        {
            return EndLabelQuickPrintResult.Failed;
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

internal sealed class WebView2PrintHostWindow : Window
{
    private readonly WebView2 _webView;

    public WebView2PrintHostWindow()
    {
        Width = 1;
        Height = 1;
        Left = -10000;
        Top = -10000;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;

        _webView = new WebView2();
        Content = _webView;
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        await _webView.EnsureCoreWebView2Async();
        await WaitForDomReadyAsync(cancellationToken);
    }

    public async Task NavigateToStringAsync(string html, CancellationToken cancellationToken)
    {
        _webView.NavigateToString(html);
        await WaitForDomReadyAsync(cancellationToken);
    }

    public async Task<bool> TryPrintToPrinterAsync(string printerName, int copies, CancellationToken cancellationToken)
    {
        if (_webView.CoreWebView2 is null)
        {
            return false;
        }

        for (var i = 0; i < copies; i++)
        {
            var settings = _webView.CoreWebView2.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.PrinterName = printerName;

            await _webView.CoreWebView2.PrintAsync(settings);

            await Task.Delay(120, cancellationToken);
        }

        return true;
    }

    private async Task WaitForDomReadyAsync(CancellationToken cancellationToken)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        for (var i = 0; i < 50; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = await _webView.ExecuteScriptAsync("document.readyState");
            if (state.Contains("complete"))
            {
                return;
            }

            await Task.Delay(80, cancellationToken);
        }
    }
}
