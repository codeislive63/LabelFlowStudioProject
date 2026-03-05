using LabelFlowStudio.Desktop.Printing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Windows;

namespace LabelFlowStudio.Desktop;

/// <summary>
/// Вспомогательный сервис бесшумной печати HTML через WebView2
/// </summary>
internal static class SilentHtmlPrinter
{
    /// <summary>
    /// Печатает HTML документ на выбранный принтер
    /// </summary>
    public static async Task<bool> PrintHtmlAsync(
        string html,
        string printerName,
        int copies,
        Window? owner,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(printerName))
        {
            return false;
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

            return await hostWindow.TryPrintToPrinterAsync(printerName, copies, cancellationToken);
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                hostWindow?.DisposeWebView();
                hostWindow?.Close();
            }
            catch
            {
            }
        }
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


        public void DisposeWebView()
        {
            try
            {
                Content = null;
                _webView.Dispose();
            }
            catch
            {
            }
        }

        // Инициализирует WebView2 окружение для печати
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

        // Загружает HTML в WebView2 и ожидает завершение навигации
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

        // Печатает текущий HTML на выбранный принтер нужное число копий
        public async Task<bool> TryPrintToPrinterAsync(string printerName, int copies, CancellationToken cancellationToken)
        {
            if (_webView.CoreWebView2 is null)
            {
                return false;
            }

            if (!PrinterDiscovery.IsPrinterInstalled(printerName))
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
                settings.ShouldPrintBackgrounds = true;
                settings.ShouldPrintHeaderAndFooter = false;

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
