using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.BoxProcessing;
using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.Templates;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Windows;

namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// Основная модель представления экрана обработки коробов
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private static readonly TimeSpan ScannerDeduplicationWindow = TimeSpan.FromSeconds(2);

    private readonly IBoxProcessingService _boxProcessingService;
    private readonly IBoxScanner _boxScanner;
    private readonly ILogger<MainViewModel> _logger;

    private readonly SemaphoreSlim _scannerGate = new(1, 1);
    private readonly SynchronizationContext? _uiContext;

    private BoxProcessingResponse? _lastSuccessfulResponse;
    private string _lastSuccessfulTenam = string.Empty;

    private BoxProcessingResponse? _lastLoadedResponse;
    private string _lastLoadedTenam = string.Empty;

    private WorkMode _nextRequestMode = WorkMode.Manual;
    private WorkMode _currentWorkMode = WorkMode.Manual;

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
        _uiContext = SynchronizationContext.Current;

        Records = new ObservableCollection<LabelRecord>();

        LoadRecordsCommand = new AsyncCommand(LoadRecordsAsync, CanLoadRecords, HandleCommandException);
        OpenEndLabelPreviewCommand = new AsyncCommand(OpenEndLabelPreviewAsync, CanOpenEndLabelPreview, HandleCommandException);
        OpenStuffingSheetPreviewCommand = new AsyncCommand(OpenStuffingSheetPreviewAsync, CanOpenStuffingSheetPreview, HandleCommandException);

        var settings = PrintSettingsStore.LoadOrDefault();
        _currentWorkMode = settings?.WorkMode ?? WorkMode.Manual;

        StatusMessage = "Введите или отсканируйте TENAM и нажмите Enter";

        _ = InitializeScannerAsync();
    }

    /// <summary>
    /// Коллекция записей для отображения в таблице
    /// </summary>
    public ObservableCollection<LabelRecord> Records { get; }

    /// <summary>
    /// Команда загрузки записей по введенному TENAM
    /// </summary>
    public AsyncCommand LoadRecordsCommand { get; }

    /// <summary>
    /// Команда открытия предпросмотра торцевой этикетки
    /// </summary>
    public AsyncCommand OpenEndLabelPreviewCommand { get; }

    /// <summary>
    /// Команда открытия предпросмотра листа сброса
    /// </summary>
    public AsyncCommand OpenStuffingSheetPreviewCommand { get; }

    /// <summary>
    /// Текущее значение TENAM
    /// </summary>
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

    /// <summary>
    /// Текущее статусное сообщение для пользователя
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Признак активной операции обработки
    /// </summary>
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

    /// <summary>
    /// Последний успешно обработанный TENAM
    /// </summary>
    public string LastProcessedTenam
    {
        get => _lastProcessedTenam;
        private set => SetProperty(ref _lastProcessedTenam, value);
    }

    /// <summary>
    /// Признак автоматического режима
    /// </summary>
    public bool IsAutomaticMode => CurrentWorkMode == WorkMode.Automatic;

    /// <summary>
    /// Текущий режим обработки коробов
    /// </summary>
    public WorkMode CurrentWorkMode
    {
        get => _currentWorkMode;
        set
        {
            if (!SetProperty(ref _currentWorkMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAutomaticMode));
            _ = SaveWorkModeAsync(value);
        }
    }

    /// <summary>
    /// Принимает TENAM от сканера и запускает обработку
    /// </summary>
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

        if (digitsOnly == _lastScannedTenam && (nowUtc - _lastScannedAtUtc) < ScannerDeduplicationWindow)
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

            _nextRequestMode = CurrentWorkMode == WorkMode.Automatic ? WorkMode.Automatic : WorkMode.Manual;
            Tenam = digitsOnly;

            if (LoadRecordsCommand.CanExecute(null))
            {
                LoadRecordsCommand.Execute(null);
            }
        });
    }

    // Сохраняет выбранный режим работы в настройках
    private async Task SaveWorkModeAsync(WorkMode workMode)
    {
        try
        {
            var settings = PrintSettingsStore.LoadOrDefault() ?? new PrintSettings();
            settings.WorkMode = workMode;
            await PrintSettingsStore.SaveAsync(settings, CancellationToken.None);
        }
        catch
        {
        }
    }

    // Проверяет возможность запуска загрузки
    private bool CanLoadRecords()
    {
        if (IsBusy)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(Tenam);
    }

    // Выполняет загрузку данных и обновляет состояние экрана
    private async Task LoadRecordsAsync()
    {
        var requestMode = _nextRequestMode;
        _nextRequestMode = WorkMode.Manual;

        var tenamSnapshot = Tenam?.Trim() ?? string.Empty;

        IsBusy = true;
        StatusMessage = "Загрузка";
        Records.Clear();
        Tenam = string.Empty;

        await Task.Yield();

        BoxProcessingResponse? response = null;

        try
        {
            var request = BuildRequest(tenamSnapshot, requestMode);

            response = await _boxProcessingService.ProcessAsync(request, CancellationToken.None);

            ApplyResponseState(response, tenamSnapshot);

            // Быстрая печать теперь только в режиме Automatic (скан + Enter)
            if (requestMode == WorkMode.Automatic && response is not null)
            {
                await TryAutoPrintAsync(response, tenamSnapshot);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load records");

            ApplyFailedLoadState(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Создает запрос на обработку с безопасными значениями настроек
    private static BoxProcessingRequest BuildRequest(string tenam, WorkMode requestMode)
    {
        var settings = PrintSettingsStore.LoadOrDefault() ?? new PrintSettings();

        return new BoxProcessingRequest(
            Tenam: tenam,
            Mode: requestMode,
            ShouldPrintEndLabels: settings.PrintEndLabelEnabled,
            ShouldPrintStuffingSheet: settings.PrintStuffingSheetEnabled
        );
    }

    // Применяет результат обработки к состоянию экрана
    private void ApplyResponseState(BoxProcessingResponse response, string tenamSnapshot)
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
    }

    // Применяет состояние ошибки после неудачной загрузки
    private void ApplyFailedLoadState(string errorMessage)
    {
        StatusMessage = errorMessage;
        _lastLoadedResponse = null;
        _lastLoadedTenam = string.Empty;
        _lastSuccessfulResponse = null;
        _lastSuccessfulTenam = string.Empty;
        LastProcessedTenam = string.Empty;

        OpenEndLabelPreviewCommand.RaiseCanExecuteChanged();
        OpenStuffingSheetPreviewCommand.RaiseCanExecuteChanged();
    }

    // Выполняет бесшумную автопечать после успешной обработки
    private async Task TryAutoPrintAsync(BoxProcessingResponse response, string tenam)
    {
        // В fast-режиме никаких попапов, только статус
        var settings = PrintSettingsStore.LoadOrDefault();

        if (settings is null || !settings.IsComplete)
        {
            StatusMessage = "Не настроены принтеры для быстрой печати";
            return;
        }

        if (!settings.PrintEndLabelEnabled && !settings.PrintStuffingSheetEnabled)
        {
            StatusMessage = "Автопечать отключена в настройках";
            return;
        }

        // Сначала печатаем лист сброса (если включено), затем торцевую этикетку.
        if (settings.PrintStuffingSheetEnabled && (response.ShouldPrintDropSheet || response.ShouldPrintEmptyDropSheet))
        {
            StatusMessage = "Печать листа сброса";
            var okSheet = await PrintStuffingSheetSilentAsync(response, tenam, settings.StuffingSheetPrinterName, settings.StuffingSheetCopies);
            if (!okSheet)
            {
                StatusMessage = "Не удалось напечатать лист сброса";
                return;
            }
        }

        if (settings.PrintEndLabelEnabled && response.Status == BoxProcessingStatus.Success && response.ShouldPrintEndLabels)
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

    // Проверяет доступность предпросмотра торцевой этикетки
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

    // Открывает окно предпросмотра торцевой этикетки
    private Task OpenEndLabelPreviewAsync()
    {
        var response = _lastSuccessfulResponse;

        if (response is null)
        {
            return Task.CompletedTask;
        }

        var tenam = _lastSuccessfulTenam;

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

        return Task.CompletedTask;
    }

    // Проверяет доступность предпросмотра листа сброса
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

    // Открывает окно предпросмотра листа сброса
    private Task OpenStuffingSheetPreviewAsync()
    {
        var response = _lastLoadedResponse;

        if (response is null)
        {
            return Task.CompletedTask;
        }

        var tenam = _lastLoadedTenam;

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

        return Task.CompletedTask;
    }

    // Печатает торцевую этикетку без отображения окна настроек
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

        if (!PrinterDiscovery.IsPrinterInstalled(printerName))
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

    // Печатает лист сброса без отображения окна настроек
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

        if (!PrinterDiscovery.IsPrinterInstalled(printerName))
        {
            return false;
        }

        if (copies <= 0)
        {
            copies = 1;
        }

        string html;

        if (!BoxProcessingResponseInspector.HasWeight(response))
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

    // Инициализирует подключение к сканеру
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

    // Гарантирует запуск сканера и подписку на события
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

    // Отвязывает обработчик событий сканера после ошибки запуска
    private Task FailScannerStartAsync()
    {
        if (_isScannerSubscribed)
        {
            _boxScanner.BoxNumberReceived -= OnBoxNumberReceived;
            _isScannerSubscribed = false;
        }

        return Task.CompletedTask;
    }

    // Обработчик события получения номера короба от сканера
    private void OnBoxNumberReceived(object? sender, BoxNumberReceivedEventArgs eventArgs)
    {
        ReceiveTenamFromScanner(eventArgs.BoxNumber);
    }

    // Выполняет действие в захваченном UI контексте
    private Task RunOnUiThreadAsync(Action action)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _uiContext.Post(_ =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);

        return completion.Task;
    }

    // Выполняет асинхронное действие в захваченном UI контексте
    private Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            return action();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _uiContext.Post(async _ =>
        {
            try
            {
                await action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);

        return completion.Task;
    }

    // Преобразует ошибку команды в пользовательский статус
    private void HandleCommandException(Exception exception)
    {
        _ = RunOnUiThreadAsync(() =>
        {
            StatusMessage = exception.Message;
        });
    }
}
