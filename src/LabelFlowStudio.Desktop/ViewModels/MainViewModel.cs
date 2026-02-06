using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Devices.BoxScanner;
using LabelFlowStudio.Printing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Windows;

namespace LabelFlowStudio.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IBoxProcessingService _boxProcessingService;
    private readonly IPrintService _printService;
    private readonly IBoxScanner _boxScanner;
    private readonly ILogger<MainViewModel> _logger;

    private readonly SemaphoreSlim _scannerGate = new(1, 1);

    private BoxProcessingResponse? _lastSuccessfulResponse;
    private string _lastSuccessfulTenam = string.Empty;

    private WorkMode _mode = WorkMode.Manual;

    private string _tenam = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private bool _shouldPrintEndLabels;
    private bool _isScannerSubscribed;

    private const bool IsDropSheetPrintingEnabled = false;

    public MainViewModel(
        IBoxProcessingService boxProcessingService,
        IPrintService printService,
        IBoxScanner boxScanner,
        ILogger<MainViewModel> logger)
    {
        _boxProcessingService = boxProcessingService ?? throw new ArgumentNullException(nameof(boxProcessingService));
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
        _boxScanner = boxScanner ?? throw new ArgumentNullException(nameof(boxScanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Records = new ObservableCollection<LabelRecord>();

        LoadRecordsCommand = new AsyncCommand(LoadRecordsAsync, CanLoadRecords, HandleCommandException);
        OpenEndLabelPreviewCommand = new AsyncCommand(OpenEndLabelPreviewAsync, CanOpenEndLabelPreview, HandleCommandException);
    }

    public ObservableCollection<LabelRecord> Records { get; }

    public AsyncCommand LoadRecordsCommand { get; }
    public AsyncCommand OpenEndLabelPreviewCommand { get; }

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
            }
        }
    }

    public bool ShouldPrintEndLabels
    {
        get => _shouldPrintEndLabels;
        set => SetProperty(ref _shouldPrintEndLabels, value);
    }

    public WorkMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsManual));
                OnPropertyChanged(nameof(IsAutomatic));

                _ = UpdateScannerStateAsync();
            }
        }
    }

    public bool IsManual
    {
        get => _mode == WorkMode.Manual;
        set
        {
            if (value)
            {
                Mode = WorkMode.Manual;
            }
        }
    }

    public bool IsAutomatic
    {
        get => _mode == WorkMode.Automatic;
        set
        {
            if (value)
            {
                Mode = WorkMode.Automatic;
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
                Mode: Mode,
                ShouldPrintEndLabels: ShouldPrintEndLabels
            );

            var response = await _boxProcessingService.ProcessAsync(request, CancellationToken.None);

            await RunOnUiThreadAsync(() =>
            {
                foreach (var record in response.Records)
                {
                    Records.Add(record);
                }

                StatusMessage = response.Message;

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
            });

            await TryPrintAsync(response, tenamSnapshot);
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                IsBusy = false;
            });
        }
    }

    private async Task TryPrintAsync(BoxProcessingResponse response, string tenam)
    {
        try
        {
            if (IsDropSheetPrintingEnabled)
            {
                if (response.ShouldPrintEmptyDropSheet)
                {
                    await _printService.PrintEmptyDropSheetAsync(tenam, CancellationToken.None);
                }

                if (response.ShouldPrintDropSheet)
                {
                    await _printService.PrintDropSheetAsync(response, tenam, CancellationToken.None);
                }
            }

            if (response.ShouldPrintEndLabels)
            {
                await _printService.PrintEndLabelAsync(response, tenam, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = "Печать отменена";
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Printing failed");

            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = "Ошибка печати";
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
        var weight = response.Weight;

        return RunOnUiThreadAsync(() =>
        {
            var window = new EndLabelTemplatePreviewWindow(tenam, weight)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            window.ShowDialog();
        });
    }

    private async Task UpdateScannerStateAsync()
    {
        try
        {
            await _scannerGate.WaitAsync();

            try
            {
                if (Mode == WorkMode.Automatic)
                {
                    await EnsureScannerStartedAsync();
                }
                else
                {
                    await EnsureScannerStoppedAsync();
                }
            }
            finally
            {
                _scannerGate.Release();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update scanner state");
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

            if (Mode == WorkMode.Automatic)
            {
                Mode = WorkMode.Manual;
            }
        });
    }

    private async Task EnsureScannerStoppedAsync()
    {
        try
        {
            if (_boxScanner.IsRunning)
            {
                await _boxScanner.StopAsync(CancellationToken.None);

                await RunOnUiThreadAsync(() =>
                {
                    StatusMessage = "Сканер остановлен";
                });
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to stop box scanner");

            await RunOnUiThreadAsync(() =>
            {
                StatusMessage = "Не удалось остановить сканер";
            });
        }
        finally
        {
            if (_isScannerSubscribed)
            {
                _boxScanner.BoxNumberReceived -= OnBoxNumberReceived;
                _isScannerSubscribed = false;
            }
        }
    }

    private void OnBoxNumberReceived(object? sender, BoxNumberReceivedEventArgs eventArgs)
    {
        _ = RunOnUiThreadAsync(() =>
        {
            if (Mode != WorkMode.Automatic)
            {
                return;
            }

            if (IsBusy)
            {
                return;
            }

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
