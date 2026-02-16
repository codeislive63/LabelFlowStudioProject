using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Drawing.Printing;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LabelFlowStudio.Desktop;

public enum StuffingSheetQuickPrintResult
{
    PrintedToPreferred,
    PrintedToSelected,
    Cancelled,
    Failed
}

public static class StuffingSheetQuickPrinter
{
    private const string PreferredPrinterName = "Kyocera ECOSYS MA4500x111";
    private const int PreferredCopies = 1;

    public static async Task<StuffingSheetQuickPrintResult> PrintHtmlAsync(
        string html,
        Window? owner,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ArgumentException("HTML is required", nameof(html));
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

            if (await hostWindow.TryPrintToPrinterAsync(PreferredPrinterName, PreferredCopies, cancellationToken))
            {
                return StuffingSheetQuickPrintResult.PrintedToPreferred;
            }

            var selection = ShowPrinterSelection(owner);
            if (selection is null)
            {
                return StuffingSheetQuickPrintResult.Cancelled;
            }

            if (await hostWindow.TryPrintToPrinterAsync(selection.PrinterName, selection.Copies, cancellationToken))
            {
                return StuffingSheetQuickPrintResult.PrintedToSelected;
            }

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

    private sealed record PrinterSelection(string PrinterName, int Copies);

    private static PrinterSelection? ShowPrinterSelection(Window? owner)
    {
        try
        {
            owner?.Activate();
        }
        catch
        {
        }

        var dialog = new PrintDialog
        {
            UserPageRangeEnabled = false
        };

        var ok = dialog.ShowDialog();
        if (ok != true)
        {
            return null;
        }

        var printerName = dialog.PrintQueue?.Name;

        if (string.IsNullOrWhiteSpace(printerName))
        {
            printerName = dialog.PrintQueue?.FullName;
        }

        if (string.IsNullOrWhiteSpace(printerName))
        {
            return null;
        }

        var copies = dialog.PrintTicket?.CopyCount ?? 1;
        if (copies < 1)
        {
            copies = 1;
        }

        return new PrinterSelection(printerName, copies);
    }

    private sealed class WebView2PrintHostWindow : Window
    {
        private readonly WebView2 _webView;

        public WebView2PrintHostWindow()
        {
            Width = 1;
            Height = 1;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            AllowsTransparency = true;
            Opacity = 0;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -10000;
            Top = -10000;

            _webView = new WebView2();
            Content = _webView;
        }

        public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_webView.CoreWebView2 is not null)
            {
                return;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userDataFolder = Path.Combine(
                localAppData,
                "LabelFlowStudio",
                "WebView2",
                $"pid-{Environment.ProcessId}"
            );

            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: null
            );

            await _webView.EnsureCoreWebView2Async(environment);

            cancellationToken.ThrowIfCancellationRequested();
        }

        public async Task NavigateToStringAsync(string html, CancellationToken cancellationToken)
        {
            if (_webView.CoreWebView2 is null)
            {
                throw new InvalidOperationException("WebView2 is not initialized");
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                _webView.NavigationCompleted -= Handler;
                tcs.TrySetResult(args.IsSuccess);
            }

            _webView.NavigationCompleted += Handler;

            using var reg = cancellationToken.Register(() =>
            {
                _webView.NavigationCompleted -= Handler;
                tcs.TrySetCanceled(cancellationToken);
            });

            _webView.CoreWebView2.NavigateToString(html);

            var ok = await tcs.Task;

            if (!ok)
            {
                throw new InvalidOperationException("Не удалось отрендерить HTML в WebView2");
            }

            await Task.Delay(120, cancellationToken);
        }

        public static bool IsPrinterInstalled(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
            {
                return false;
            }

            foreach (var installedPrinter in PrinterSettings.InstalledPrinters)
            {
                if (installedPrinter is string name &&
                    string.Equals(name, printerName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public async Task<bool> TryPrintToPrinterAsync(string printerName, int copies, CancellationToken cancellationToken)
        {
            if (_webView.CoreWebView2 is null)
            {
                return false;
            }

            if (!IsPrinterInstalled(printerName))
            {
                return false;
            }

            if (copies < 1)
            {
                copies = 1;
            }

            for (var i = 0; i < copies; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var settings = _webView.CoreWebView2.Environment.CreatePrintSettings();
                settings.PrinterName = printerName;

                var status = await _webView.CoreWebView2.PrintAsync(settings);
                if (status != CoreWebView2PrintStatus.Succeeded)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
