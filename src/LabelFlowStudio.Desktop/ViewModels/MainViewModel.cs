using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Desktop.Templates;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;

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

    private bool _isEndLabelQuickMode;
    private bool _isStuffingSheetQuickMode;

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

    /// <summary>
    /// Если true — кнопка "Торцевая этикетка" печатает сразу на Zebra.
    /// Если Zebra недоступна — открываем выбор принтера.
    /// </summary>
    public bool IsEndLabelQuickMode
    {
        get => _isEndLabelQuickMode;
        set => SetProperty(ref _isEndLabelQuickMode, value);
    }

    /// <summary>
    /// Если true — кнопка "Лист сброса" печатает сразу на Kyocera.
    /// Если Kyocera недоступна — открываем выбор принтера.
    /// </summary>
    public bool IsStuffingSheetQuickMode
    {
        get => _isStuffingSheetQuickMode;
        set => SetProperty(ref _isStuffingSheetQuickMode, value);
    }

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
        });

        await Task.Yield();

        try
        {
            var request = new BoxProcessingRequest(
                Tenam: tenamSnapshot,
                Mode: requestMode,
                ShouldPrintEndLabels: true
            );

            var response = await Task.Run(() => _boxProcessingService.ProcessAsync(request, CancellationToken.None));

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

                    Tenam = string.Empty;
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
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                IsBusy = false;
            });
        }
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

        if (IsEndLabelQuickMode)
        {
            await PrintEndLabelQuickAsync(response, tenam);
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            _endLabelPreviewWindow?.Close();
            _endLabelPreviewWindow = null;

            var window = new EndLabelTemplatePreviewWindow(response, tenam)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
                ShowInTaskbar = true,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
            };

            window.Closed += (_, _) => _endLabelPreviewWindow = null;

            _endLabelPreviewWindow = window;

            window.Show();
            window.Activate();
        });
    }

    private async Task PrintEndLabelQuickAsync(BoxProcessingResponse response, string tenam)
    {
        await RunOnUiThreadAsync(() =>
        {
            IsBusy = true;
            StatusMessage = "Подготовка торцевой этикетки к печати";
        });

        await Task.Yield();

        try
        {
            var templateText = await EndLabelTemplateStore.LoadOrCreateAsync(CancellationToken.None);
            var html = EndLabelHtmlTemplateRenderer.Render(templateText, response, tenam);

            var result = await EndLabelQuickPrinter.PrintHtmlAsync(
                html,
                owner: System.Windows.Application.Current?.MainWindow,
                cancellationToken: CancellationToken.None);

            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = result switch
                {
                    EndLabelQuickPrintResult.PrintedToPreferred => "Торцевая этикетка отправлена на печать (Zebra)",
                    EndLabelQuickPrintResult.PrintedToSelected => "Торцевая этикетка отправлена на печать",
                    EndLabelQuickPrintResult.Cancelled => "Печать торцевой этикетки отменена",
                    _ => "Не удалось напечатать торцевую этикетку"
                };
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

        if (IsStuffingSheetQuickMode)
        {
            await PrintStuffingSheetQuickAsync(response, tenam);
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            _stuffingSheetPreviewWindow?.Close();
            _stuffingSheetPreviewWindow = null;

            var window = new StuffingSheetTemplatePreviewWindow(response, tenam)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
                ShowInTaskbar = true,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
            };

            window.Closed += (_, _) => _stuffingSheetPreviewWindow = null;

            _stuffingSheetPreviewWindow = window;

            window.Show();
            window.Activate();
        });
    }

    private async Task PrintStuffingSheetQuickAsync(BoxProcessingResponse response, string tenam)
    {
        await RunOnUiThreadAsync(() =>
        {
            IsBusy = true;
            StatusMessage = "Подготовка листа сброса к печати";
        });

        await Task.Yield();

        try
        {
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

            var result = await StuffingSheetQuickPrinter.PrintHtmlAsync(
                html,
                owner: System.Windows.Application.Current?.MainWindow,
                cancellationToken: CancellationToken.None);

            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = result switch
                {
                    StuffingSheetQuickPrintResult.PrintedToPreferred => "Лист сброса отправлен на печать (Kyocera)",
                    StuffingSheetQuickPrintResult.PrintedToSelected => "Лист сброса отправлен на печать",
                    StuffingSheetQuickPrintResult.Cancelled => "Печать листа сброса отменена",
                    _ => "Не удалось напечатать лист сброса"
                };
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

    private void HandleCommandException(Exception exception)
    {
        _ = RunOnUiThreadAsync(() =>
        {
            StatusMessage = exception.Message;
        });
    }
}
