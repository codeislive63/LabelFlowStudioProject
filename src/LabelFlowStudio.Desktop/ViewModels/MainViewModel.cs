using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace LabelFlowStudio.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IBoxProcessingService _boxProcessingService;
    private readonly IBoxScanner _boxScanner;
    private readonly ILogger<MainViewModel> _logger;

    private readonly SemaphoreSlim _scannerGate = new SemaphoreSlim(1, 1);

    private WorkMode _mode = WorkMode.Manual;

    private string _tenam = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private bool _shouldPrintEndLabels;
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
    }

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

    public ObservableCollection<LabelRecord> Records { get; }

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
            }
        }
    }

    public AsyncCommand LoadRecordsCommand { get; }

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
        IsBusy = true;

        try
        {
            StatusMessage = "Загрузка";
            Records.Clear();

            var request = new BoxProcessingRequest(
                Tenam: Tenam,
                Mode: Mode,
                ShouldPrintEndLabels: ShouldPrintEndLabels
            );

            var response = await _boxProcessingService.ProcessAsync(request, CancellationToken.None);

            foreach (var record in response.Records)
            {
                Records.Add(record);
            }

            StatusMessage = response.Message;

            if (response.Status == BoxProcessingStatus.Success)
            {
                Tenam = string.Empty;
            }
        }
        finally
        {
            IsBusy = false;
        }
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update scanner state");
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
                StatusMessage = "Сканер запущен";
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start box scanner");
            StatusMessage = "Не удалось запустить сканер";
        }
    }

    private async Task EnsureScannerStoppedAsync()
    {
        try
        {
            if (_boxScanner.IsRunning)
            {
                await _boxScanner.StopAsync(CancellationToken.None);
                StatusMessage = "Сканер остановлен";
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to stop box scanner");
            StatusMessage = "Не удалось остановить сканер";
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
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(() =>
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


    private void HandleCommandException(Exception exception)
    {
        StatusMessage = exception.Message;
    }
}
