using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.Templates;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace LabelFlowStudio.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IBoxProcessingService _boxProcessingService;
    private readonly IBoxScanner _boxScanner;
    private readonly ILogger<MainViewModel> _logger;

    private readonly SemaphoreSlim _scannerGate = new(1, 1);

    private BoxProcessingResponse? _lastSuccessfulResponse;
    private string _lastSuccessfulTenam = string.Empty;

    private BoxProcessingResponse? _lastLoadedResponse;
    private string _lastLoadedTenam = string.Empty;

    private WorkMode _nextRequestMode = WorkMode.Manual;

    private EndLabelTemplatePreviewWindow? _endLabelPreviewWindow;
    private StuffingSheetTemplatePreviewWindow? _stuffingSheetPreviewWindow;

    private string _tenam = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private bool _isScannerSubscribed;

    private string _lastProcessedTenam = string.Empty;

    private string _lastScannedTenam = string.Empty;
    private DateTime _lastScannedAtUtc;

    public MainViewModel(
        IBoxProcessingService boxProcessingService,
        IBoxScanner boxScanner,
        ILogger<MainViewModel> logger)
    {
        _boxProcessingService = boxProcessingService ?? throw new ArgumentNullException(nameof(boxProcessingService));
        _boxScanner = boxScanner ?? throw new ArgumentNullException(nameof(boxScanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Records = new ObservableCollection<LabelRecord>();

        LoadRecordsCommand = new AsyncCommand(LoadRecordsAsync, CanLoadRecords, HandleCommandException);
        OpenEndLabelPreviewCommand = new AsyncCommand(OpenEndLabelPreviewAsync, CanOpenEndLabelPreview, HandleCommandException);
        OpenStuffingSheetPreviewCommand = new AsyncCommand(OpenStuffingSheetPreviewAsync, CanOpenStuffingSheetPreview, HandleCommandException);

        StatusMessage = "Введите или отсканируйте TENAM и нажмите Enter";

        _ = InitializeScannerAsync();
    }

    public ObservableCollection<LabelRecord> Records { get; }

    public AsyncCommand LoadRecordsCommand { get; }
    public AsyncCommand OpenEndLabelPreviewCommand { get; }
    public AsyncCommand OpenStuffingSheetPreviewCommand { get; }

    public string Tenam
    {
        get => _tenam;
        set
        {
            var digitsOnly = new string((value ?? string.Empty)
                .Where(char.IsDigit)
                .ToArray());

            if (SetProperty(ref _tenam, digitsOnly))
            {
                LoadRecordsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                LoadRecordsCommand.RaiseCanExecuteChanged();
                OpenEndLabelPreviewCommand.RaiseCanExecuteChanged();
                OpenStuffingSheetPreviewCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LastProcessedTenam
    {
        get => _lastProcessedTenam;
        private set => SetProperty(ref _lastProcessedTenam, value);
    }

    public void ReceiveTenamFromScanner(string boxNumber)
    {
        var digitsOnly = new string((boxNumber ?? string.Empty)
            .Where(char.IsDigit)
            .ToArray());

        if (string.IsNullOrWhiteSpace(digitsOnly))
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;

        if (digitsOnly == _lastScannedTenam && (nowUtc - _lastScannedAtUtc) < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastScannedTenam = digitsOnly;
        _lastScannedAtUtc = nowUtc;

        _ = RunOnUiThreadAsync(() =>
        {
            if (IsBusy)
            {
                return;
            }

            _nextRequestMode = WorkMode.Automatic;
            Tenam = digitsOnly;

            if (LoadRecordsCommand.CanExecute(null))
            {
                LoadRecordsCommand.Execute(null);
            }
        });
    }

    private bool CanLoadRecords()
    {
        if (IsBusy)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(Tenam);
    }

    private async Task LoadRecordsAsync()
    {
        var requestMode = _nextRequestMode;
        _nextRequestMode = WorkMode.Manual;

        var tenamSnapshot = Tenam?.Trim() ?? string.Empty;

        await RunOnUiThreadAsync(() =>
        {
            IsBusy = true;
            StatusMessage = "Загрузка";
            Records.Clear();
            Tenam = string.Empty;
        });

        await Task.Yield();

        BoxProcessingResponse? response = null;

        try
        {
            var request = new BoxProcessingRequest(
                Tenam: tenamSnapshot,
                Mode: requestMode,
                ShouldPrintEndLabels: true
            );

            response = await Task.Run(() => _boxProcessingService.ProcessAsync(request, CancellationToken.None));

            await RunOnUiThreadAsync(() =>
            {
                foreach (var record in response.Records)
                {
                    Records.Add(record);
                }

                StatusMessage = response.Message;

                if (response.Records.Count > 0)
                {
                    _lastLoadedResponse = response;
                    _lastLoadedTenam = tenamSnapshot;
                }
                else
                {
                    _lastLoadedResponse = null;
                    _lastLoadedTenam = string.Empty;
                }

                if (response.Status == BoxProcessingStatus.Success)
                {
                    _lastSuccessfulResponse = response;
                    _lastSuccessfulTenam = tenamSnapshot;

                    LastProcessedTenam = tenamSnapshot;
                }
                else
                {
                    _lastSuccessfulResponse = null;
                    _lastSuccessfulTenam = string.Empty;

                    LastProcessedTenam = string.Empty;
                }

                OpenEndLabelPreviewCommand.RaiseCanExecuteChanged();
                OpenStuffingSheetPreviewCommand.RaiseCanExecuteChanged();
            });

            // Быстрая печать теперь только в режиме Automatic (скан + Enter)
            if (requestMode == WorkMode.Automatic && response is not null)
            {
                await RunOnUiThreadAsync(async () =>
                {
                    await TryAutoPrintAsync(response, tenamSnapshot);
                });
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load records");

            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = exception.Message;
                _lastLoadedResponse = null;
                _lastLoadedTenam = string.Empty;
                _lastSuccessfulResponse = null;
                _lastSuccessfulTenam = string.Empty;
                LastProcessedTenam = string.Empty;

                OpenEndLabelPreviewCommand.RaiseCanExecuteChanged();
                OpenStuffingSheetPreviewCommand.RaiseCanExecuteChanged();
            });
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                IsBusy = false;
            });
        }
    }

    private async Task TryAutoPrintAsync(BoxProcessingResponse response, string tenam)
    {
        // В fast-режиме никаких попапов, только статус
        var settings = Printing.PrintSettingsStore.LoadOrDefault();

        if (!settings.IsComplete)
        {
            StatusMessage = "Не настроены принтеры для быстрой печати";
            return;
        }

        // Сначала печатаем лист сброса (если есть записи), затем торцевую этикетку.
        if (response.Records.Count > 0)
        {
            StatusMessage = "Печать листа сброса";
            var okSheet = await PrintStuffingSheetSilentAsync(response, tenam, settings.StuffingSheetPrinterName, settings.StuffingSheetCopies);
            if (!okSheet)
            {
                StatusMessage = "Не удалось напечатать лист сброса";
                return;
            }
        }

        if (response.Status == BoxProcessingStatus.Success)
        {
            StatusMessage = "Печать торцевой этикетки";
            var okEndLabel = await PrintEndLabelSilentAsync(response, tenam, settings.EndLabelPrinterName, settings.EndLabelCopies);
            if (!okEndLabel)
            {
                StatusMessage = "Не удалось напечатать торцевую этикетку";
                return;
            }
        }

        StatusMessage = "Отправлено на печать";
    }

    private bool CanOpenEndLabelPreview()
    {
        if (IsBusy)
        {
            return false;
        }

        if (_lastSuccessfulResponse is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_lastSuccessfulTenam);
    }

    private async Task OpenEndLabelPreviewAsync()
    {
        var response = _lastSuccessfulResponse;

        if (response is null)
        {
            return;
        }

        var tenam = _lastSuccessfulTenam;

        await RunOnUiThreadAsync(() =>
        {
            _endLabelPreviewWindow?.Close();
            _endLabelPreviewWindow = null;

            var window = new EndLabelTemplatePreviewWindow(response, tenam)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
                ShowInTaskbar = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            window.Closed += (_, _) => _endLabelPreviewWindow = null;

            _endLabelPreviewWindow = window;

            window.Show();
            window.Activate();
        });
    }

    private bool CanOpenStuffingSheetPreview()
    {
        if (IsBusy)
        {
            return false;
        }

        if (_lastLoadedResponse is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_lastLoadedTenam);
    }

    private async Task OpenStuffingSheetPreviewAsync()
    {
        var response = _lastLoadedResponse;

        if (response is null)
        {
            return;
        }

        var tenam = _lastLoadedTenam;

        await RunOnUiThreadAsync(() =>
        {
            _stuffingSheetPreviewWindow?.Close();
            _stuffingSheetPreviewWindow = null;

            var window = new StuffingSheetTemplatePreviewWindow(response, tenam)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
                ShowInTaskbar = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            window.Closed += (_, _) => _stuffingSheetPreviewWindow = null;

            _stuffingSheetPreviewWindow = window;

            window.Show();
            window.Activate();
        });
    }

    private static async Task<bool> PrintEndLabelSilentAsync(
        BoxProcessingResponse response,
        string tenam,
        string printerName,
        int copies)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return false;
        }

        if (!Printing.PrinterDiscovery.IsPrinterInstalled(printerName))
        {
            return false;
        }

        if (copies <= 0)
        {
            copies = 1;
        }

        var templateText = await EndLabelTemplateStore.LoadOrCreateAsync(CancellationToken.None);
        var html = EndLabelHtmlTemplateRenderer.Render(templateText, response, tenam);

        return await SilentHtmlPrinter.PrintHtmlAsync(
            html: html,
            printerName: printerName,
            copies: copies,
            owner: System.Windows.Application.Current?.MainWindow,
            cancellationToken: CancellationToken.None);
    }

    private static async Task<bool> PrintStuffingSheetSilentAsync(
        BoxProcessingResponse response,
        string tenam,
        string printerName,
        int copies)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return false;
        }

        if (!Printing.PrinterDiscovery.IsPrinterInstalled(printerName))
        {
            return false;
        }

        if (copies <= 0)
        {
            copies = 1;
        }

        string html;

        if (!HasWeight(response))
        {
            html = await EmptyPageTemplateStore.LoadOrCreateAsync(CancellationToken.None);
        }
        else
        {
            var template = await StuffingSheetTemplateStore.LoadOrCreateAsync(CancellationToken.None);
            html = StuffingSheetHtmlTemplateRenderer.Render(template, response, tenam);
        }

        return await SilentHtmlPrinter.PrintHtmlAsync(
            html: html,
            printerName: printerName,
            copies: copies,
            owner: System.Windows.Application.Current?.MainWindow,
            cancellationToken: CancellationToken.None);
    }

    private static bool HasWeight(BoxProcessingResponse response)
    {
        if (response.Weight.HasValue && response.Weight.Value > 0)
        {
            return true;
        }

        if (response.Records.Count == 0)
        {
            return false;
        }

        var brutto = response.Records[0].Brutto;
        return brutto.HasValue && brutto.Value > 0;
    }

    private async Task InitializeScannerAsync()
    {
        try
        {
            await _scannerGate.WaitAsync();

            try
            {
                await EnsureScannerStartedAsync();
            }
            finally
            {
                _scannerGate.Release();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to initialize scanner");
        }
    }

    private async Task EnsureScannerStartedAsync()
    {
        try
        {
            if (!_isScannerSubscribed)
            {
                _boxScanner.BoxNumberReceived += OnBoxNumberReceived;
                _isScannerSubscribed = true;
            }

            if (!_boxScanner.IsRunning)
            {
                await _boxScanner.StartAsync(CancellationToken.None);
            }
        }
        catch (OptionsValidationException exception)
        {
            _logger.LogInformation(exception, "Box scanner configuration is invalid, fallback to keyboard scanner");
            await FailScannerStartAsync();
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogInformation(exception, "Box scanner is not configured, fallback to keyboard scanner");
            await FailScannerStartAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to start box scanner, fallback to keyboard scanner");
            await FailScannerStartAsync();
        }
    }

    private Task FailScannerStartAsync()
    {
        if (_isScannerSubscribed)
        {
            _boxScanner.BoxNumberReceived -= OnBoxNumberReceived;
            _isScannerSubscribed = false;
        }

        return Task.CompletedTask;
    }

    private void OnBoxNumberReceived(object? sender, BoxNumberReceivedEventArgs eventArgs)
    {
        ReceiveTenamFromScanner(eventArgs.BoxNumber);
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static Task RunOnUiThreadAsync(Func<Task> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        return dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private void HandleCommandException(Exception exception)
    {
        _ = RunOnUiThreadAsync(() =>
        {
            StatusMessage = exception.Message;
        });
    }
}

internal static class SilentHtmlPrinter
{
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
