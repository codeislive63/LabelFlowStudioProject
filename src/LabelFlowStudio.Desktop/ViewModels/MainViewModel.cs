using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Commands;
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

    private string _tenam = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private bool _isScannerSubscribed;

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
            if (SetProperty(ref _tenam, value))
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

        try
        {
            var request = new BoxProcessingRequest(
                Tenam: tenamSnapshot,
                Mode: requestMode,
                ShouldPrintEndLabels: true
            );

            var response = await _boxProcessingService.ProcessAsync(request, CancellationToken.None);

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

                    Tenam = string.Empty;
                }
                else
                {
                    _lastSuccessfulResponse = null;
                    _lastSuccessfulTenam = string.Empty;
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

    private Task OpenEndLabelPreviewAsync()
    {
        var response = _lastSuccessfulResponse;

        if (response is null)
        {
            return Task.CompletedTask;
        }

        var tenam = _lastSuccessfulTenam;

        return RunOnUiThreadAsync(() =>
        {
            var window = new EndLabelTemplatePreviewWindow(response, tenam)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            window.ShowDialog();
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

    private Task OpenStuffingSheetPreviewAsync()
    {
        var response = _lastLoadedResponse;

        if (response is null)
        {
            return Task.CompletedTask;
        }

        var tenam = _lastLoadedTenam;

        return RunOnUiThreadAsync(() =>
        {
            var window = new StuffingSheetTemplatePreviewWindow(response, tenam)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            window.ShowDialog();
        });
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
            _logger.LogError(exception, "Failed to initialize scanner");
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

                await RunOnUiThreadAsync(() =>
                {
                    StatusMessage = "Сканер запущен";
                });
            }
        }
        catch (OptionsValidationException exception)
        {
            _logger.LogError(exception, "Box scanner configuration is invalid");
            await FailScannerStartAsync();
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(exception, "Box scanner is not configured");
            await FailScannerStartAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start box scanner");

            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = "Не удалось запустить сканер";
            });
        }
    }

    private Task FailScannerStartAsync()
    {
        if (_isScannerSubscribed)
        {
            _boxScanner.BoxNumberReceived -= OnBoxNumberReceived;
            _isScannerSubscribed = false;
        }

        return RunOnUiThreadAsync(() =>
        {
            StatusMessage = "Сканер не настроен";
        });
    }

    private void OnBoxNumberReceived(object? sender, BoxNumberReceivedEventArgs eventArgs)
    {
        _ = RunOnUiThreadAsync(() =>
        {
            if (IsBusy)
            {
                return;
            }

            _nextRequestMode = WorkMode.Automatic;

            Tenam = eventArgs.BoxNumber;

            if (LoadRecordsCommand.CanExecute(null))
            {
                LoadRecordsCommand.Execute(null);
            }
        });
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
