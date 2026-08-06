using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;
using System.Windows;
using System.Windows.Threading;

namespace LabelFlowStudio.Desktop;

/// <summary>
/// Prints HTML through one reusable, off-screen WebView2 host.
/// </summary>
internal static class SilentHtmlPrinter
{
    private static readonly SemaphoreSlim PrintGate = new(1, 1);

    private static WebView2PrintHostWindow? _hostWindow;
    private static volatile bool _shutdownRequested;

    /// <summary>
    /// Prints an HTML document on the selected printer.
    /// </summary>
    public static async Task<bool> PrintHtmlAsync(
        string html,
        string printerName,
        int copies,
        Window? owner,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(printerName))
        {
            return false;
        }

        copies = Math.Max(1, copies);

        var lockTaken = false;

        try
        {
            await PrintGate.WaitAsync(cancellationToken);
            lockTaken = true;

            if (_shutdownRequested)
            {
                return false;
            }

            return await RunOnUiThreadAsync(
                () => PrintOnUiThreadAsync(html, printerName, copies, owner, cancellationToken),
                cancellationToken);
        }
        catch (OutOfMemoryException)
        {
            if (lockTaken)
            {
                try
                {
                    await DiscardHostWindowAsync();
                }
                catch
                {
                    // Preserve the original allocation failure. The process may not
                    // have enough memory left to complete WebView2 cleanup.
                }
            }

            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (lockTaken)
            {
                await DiscardHostWindowAsync();
            }

            return false;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Silent WebView2 printing failed for printer {PrinterName}", printerName);

            if (lockTaken)
            {
                await DiscardHostWindowAsync();
            }

            return false;
        }
        finally
        {
            if (lockTaken)
            {
                PrintGate.Release();
            }
        }
    }

    /// <summary>
    /// Releases the shared WebView2 controller during application shutdown.
    /// </summary>
    public static async Task ShutdownAsync()
    {
        _shutdownRequested = true;

        if (!await PrintGate.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Log.Warning("Timed out waiting for active WebView2 printing during shutdown");
            return;
        }

        try
        {
            await DiscardHostWindowAsync();
        }
        finally
        {
            PrintGate.Release();
        }
    }

    private static async Task<bool> PrintOnUiThreadAsync(
        string html,
        string printerName,
        int copies,
        Window? owner,
        CancellationToken cancellationToken)
    {
        var hostWindow = GetOrCreateHostWindow(owner);

        await hostWindow.EnsureInitializedAsync(cancellationToken);
        hostWindow.SetActiveMemoryTarget();

        try
        {
            await hostWindow.NavigateToStringAsync(html, cancellationToken);
            return await hostWindow.TryPrintToPrinterAsync(printerName, copies, cancellationToken);
        }
        finally
        {
            await hostWindow.ReleaseDocumentMemoryAsync();
        }
    }

    private static WebView2PrintHostWindow GetOrCreateHostWindow(Window? owner)
    {
        if (_hostWindow is { IsUsable: true })
        {
            return _hostWindow;
        }

        DisposeHostWindow();

        var hostWindow = new WebView2PrintHostWindow();

        if (owner is not null && owner.IsLoaded)
        {
            hostWindow.Owner = owner;
        }

        _hostWindow = hostWindow;
        hostWindow.Show();

        return hostWindow;
    }

    private static async Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            throw new InvalidOperationException("WPF dispatcher is not available");
        }

        if (dispatcher.CheckAccess())
        {
            return await action();
        }

        var nestedTask = await dispatcher.InvokeAsync(
            action,
            DispatcherPriority.Send,
            cancellationToken);

        return await nestedTask;
    }

    private static async Task DiscardHostWindowAsync()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            _hostWindow = null;
            return;
        }

        try
        {
            if (dispatcher.CheckAccess())
            {
                DisposeHostWindow();
                return;
            }

            if (dispatcher.HasShutdownStarted)
            {
                _hostWindow = null;
                return;
            }

            await dispatcher.InvokeAsync(DisposeHostWindow, DispatcherPriority.Send);
        }
        catch
        {
            _hostWindow = null;
        }
    }

    private static void DisposeHostWindow()
    {
        var hostWindow = _hostWindow;
        _hostWindow = null;
        hostWindow?.Dispose();
    }

    private sealed class WebView2PrintHostWindow : Window, IDisposable
    {
        private readonly WebView2 _webView;
        private bool _disposed;
        private bool _processFailed;

        public WebView2PrintHostWindow()
        {
            Width = 1;
            Height = 1;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;

            _webView = new WebView2();
            Content = _webView;

            Closed += OnClosed;
        }

        public bool IsUsable => !_disposed && !_processFailed;

        public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            ThrowIfUnusable();
            cancellationToken.ThrowIfCancellationRequested();

            if (_webView.CoreWebView2 is not null)
            {
                return;
            }

            var environment = await LabelFlowStudio.Desktop.Printing.WebView2EnvironmentProvider
                .GetAsync()
                .WaitAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // CoreWebView2 initialization itself is not cancellable. Await it to completion
            // before disposing the control so native initialization cannot race with Dispose().
            await _webView.EnsureCoreWebView2Async(environment);
            cancellationToken.ThrowIfCancellationRequested();

            var coreWebView = _webView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 initialization completed without a CoreWebView2 instance");

            coreWebView.ProcessFailed -= OnWebViewProcessFailed;
            coreWebView.ProcessFailed += OnWebViewProcessFailed;
            coreWebView.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        }

        public async Task NavigateToStringAsync(string html, CancellationToken cancellationToken)
        {
            ThrowIfUnusable();

            if (_webView.CoreWebView2 is null)
            {
                throw new InvalidOperationException("WebView2 is not initialized");
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args) =>
                completion.TrySetResult(args.IsSuccess);

            _webView.NavigationCompleted += Handler;

            try
            {
                _webView.CoreWebView2.NavigateToString(html);

                var succeeded = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                if (!succeeded)
                {
                    throw new InvalidOperationException("Не удалось отрендерить HTML в WebView2");
                }

                await Task.Delay(120, cancellationToken);
            }
            finally
            {
                _webView.NavigationCompleted -= Handler;
            }
        }

        public async Task<bool> TryPrintToPrinterAsync(
            string printerName,
            int copies,
            CancellationToken cancellationToken)
        {
            ThrowIfUnusable();

            if (_webView.CoreWebView2 is null || !Printing.PrinterDiscovery.IsPrinterInstalled(printerName))
            {
                return false;
            }

            for (var index = 0; index < Math.Max(1, copies); index++)
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

        public void SetActiveMemoryTarget()
        {
            if (!_disposed && _webView.CoreWebView2 is not null)
            {
                _webView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
        }

        public async Task ReleaseDocumentMemoryAsync()
        {
            if (_disposed || _webView.CoreWebView2 is null)
            {
                return;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs) =>
                completion.TrySetResult();

            try
            {
                _webView.NavigationCompleted += Handler;
                _webView.CoreWebView2.Navigate("about:blank");
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
                _webView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            }
            catch
            {
                _processFailed = true;
            }
            finally
            {
                _webView.NavigationCompleted -= Handler;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Closed -= OnClosed;

            try
            {
                if (_webView.CoreWebView2 is not null)
                {
                    _webView.CoreWebView2.ProcessFailed -= OnWebViewProcessFailed;
                }

                Content = null;
                _webView.Dispose();
            }
            catch
            {
            }

            try
            {
                Close();
            }
            catch
            {
            }
        }

        private void OnWebViewProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs eventArgs)
        {
            _processFailed = true;

            Log.Error(
                "WebView2 print process failed. Kind: {ProcessFailedKind}; reason: {ProcessFailedReason}; exit code: {ExitCode}",
                eventArgs.ProcessFailedKind,
                eventArgs.Reason,
                eventArgs.ExitCode);
        }

        private void OnClosed(object? sender, EventArgs eventArgs) => Dispose();

        private void ThrowIfUnusable()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_processFailed)
            {
                throw new InvalidOperationException("WebView2 process failed and must be recreated");
            }
        }
    }
}
