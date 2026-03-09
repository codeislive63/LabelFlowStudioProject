using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.BoxProcessing;
using LabelFlowStudio.Desktop;
using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.Templates;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;

namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// Основная модель представления экрана обработки коробов
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan ScannerDeduplicationWindow = TimeSpan.FromSeconds(2);
    private const int MaxNotifications = 50;

    private readonly IBoxProcessingService _boxProcessingService;
    private readonly IBoxScanner _boxScanner;
    private readonly ILogger<MainViewModel> _logger;

    private readonly SemaphoreSlim _scannerGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly SynchronizationContext? _uiContext;
    private CancellationTokenSource? _loadCancellation;

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
    private string _pendingScannerTenam = string.Empty;

    private DateTime _lastScannedAtUtc;

    private bool _isNotificationCenterOpen;
    private UiNotification? _selectedNotification;
    private int _notificationTabIndex;
    private int _unreadNotificationsCount;
    private bool _hasUnreadErrorNotifications;
    private bool _disposed;

    /// <summary>
    /// Создает модель представления главного экрана
    /// </summary>
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
        Notifications = new ObservableCollection<UiNotification>();
        Notifications.CollectionChanged += OnNotificationsCollectionChanged;

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

    public ObservableCollection<UiNotification> Notifications { get; }

    public IEnumerable<UiNotification> FilteredNotifications => NotificationTabIndex == 1
        ? Notifications.Where(notification => notification.IsError)
        : Notifications;

    public UiNotification? SelectedNotification
    {
        get => _selectedNotification;
        set => SetProperty(ref _selectedNotification, value);
    }

    public bool IsNotificationCenterOpen
    {
        get => _isNotificationCenterOpen;
        set
        {
            if (!SetProperty(ref _isNotificationCenterOpen, value))
            {
                return;
            }

            if (value)
            {
                UnreadNotificationsCount = 0;
                HasUnreadErrorNotifications = false;
                if (SelectedNotification is null)
                {
                    SelectedNotification = Notifications.FirstOrDefault();
                }
            }
        }
    }

    public int NotificationTabIndex
    {
        get => _notificationTabIndex;
        set
        {
            if (!SetProperty(ref _notificationTabIndex, value))
            {
                return;
            }

            OnPropertyChanged(nameof(FilteredNotifications));

            if (SelectedNotification is not null && value == 1 && !SelectedNotification.IsError)
            {
                SelectedNotification = Notifications.FirstOrDefault(notification => notification.IsError);
            }
        }
    }

    public int UnreadNotificationsCount
    {
        get => _unreadNotificationsCount;
        private set => SetProperty(ref _unreadNotificationsCount, value);
    }

    public bool HasUnreadErrorNotifications
    {
        get => _hasUnreadErrorNotifications;
        private set => SetProperty(ref _hasUnreadErrorNotifications, value);
    }

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
                _pendingScannerTenam = digitsOnly;
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
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to persist selected work mode");
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
        var cancellationToken = StartNewLoadCancellation();

        IsBusy = true;
        StatusMessage = "Загрузка";
        Records.Clear();
        Tenam = string.Empty;

        try
        {
            var request = BuildRequest(tenamSnapshot, requestMode);

            await Task.Yield();

            BoxProcessingResponse? response = await ProcessRequestWithoutUiBlockingAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (response.Status == BoxProcessingStatus.NeedWeight)
            {
                response = await RequestManualWeightAsync(response, tenamSnapshot, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }

            ApplyResponseState(response, tenamSnapshot);

            if (requestMode == WorkMode.Automatic && response is not null)
            {
                await TryAutoPrintAsync(response, tenamSnapshot, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Операция отменена";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load records");

            ApplyFailedLoadState(exception.Message);
        }
        finally
        {
            IsBusy = false;
            SchedulePendingScannerProcessing();
        }
    }

    private async Task<BoxProcessingResponse> RequestManualWeightAsync(
        BoxProcessingResponse response,
        string tenam,
        CancellationToken cancellationToken)
    {
        var weightMonitorTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var scaleResponseTask = WaitForWeightFromScalesAsync(tenam, weightMonitorTokenSource.Token);

        decimal? enteredWeight = null;
        decimal? scaleWeight = null;

        await RunOnUiThreadAsync(() =>
        {
            var dialog = new ManualWeightInputWindow(tenam)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            _ = Task.Run(async () =>
            {
                var scaleWeightValue = await scaleResponseTask;
                if (!scaleWeightValue.HasValue)
                {
                    return;
                }

                await RunOnUiThreadAsync(() =>
                {
                    if (dialog.IsVisible)
                    {
                        dialog.AcceptScaleWeight(scaleWeightValue.Value);
                    }
                });
            }, CancellationToken.None);

            var accepted = dialog.ShowDialog() == true;
            if (accepted)
            {
                enteredWeight = dialog.EnteredWeight;
                scaleWeight = dialog.ScaleWeight;
            }
        });

        weightMonitorTokenSource.Cancel();

        if (scaleWeight.HasValue)
        {
            var refreshed = await _boxProcessingService.ProcessAsync(
                BuildRequest(tenam, WorkMode.Manual),
                cancellationToken);

            if (refreshed.Status == BoxProcessingStatus.Success && refreshed.Weight.HasValue && refreshed.Weight > 0)
            {
                return refreshed with { Message = "Вес получен с весов" };
            }

            return response with { Message = "Вес с весов получен, но данные не обновились. Повторите сканирование." };
        }

        if (!enteredWeight.HasValue)
        {
            return response;
        }

        var manualWeight = Math.Round(enteredWeight.Value, 3, MidpointRounding.AwayFromZero);
        var saved = await _boxProcessingService.UpdateWeightAsync(tenam, manualWeight, cancellationToken);

        if (!saved)
        {
            AddNotification($"Не удалось сохранить вес для короба №{tenam} в БД", isError: true);
            return response with { Message = "Не удалось сохранить вес в БД" };
        }

        var settings = PrintSettingsStore.LoadOrDefault() ?? new PrintSettings();

        var updatedRecords = response.Records
            .Select(record =>
            {
                record.Brutto = manualWeight;
                return record;
            })
            .ToList();

        return response with
        {
            Status = BoxProcessingStatus.Success,
            Message = "Вес введен вручную и сохранен в БД",
            Weight = manualWeight,
            Records = updatedRecords,
            ShouldPrintDropSheet = settings.PrintStuffingSheetEnabled,
            ShouldPrintEmptyDropSheet = false,
            ShouldPrintEndLabels = settings.PrintEndLabelEnabled
        };
    }

    private async Task<decimal?> WaitForWeightFromScalesAsync(string tenam, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var currentResponse = await _boxProcessingService.ProcessAsync(
                    BuildRequest(tenam, WorkMode.Manual),
                    cancellationToken);

                if (currentResponse.Status == BoxProcessingStatus.Success
                    && currentResponse.Weight.HasValue
                    && currentResponse.Weight > 0)
                {
                    return currentResponse.Weight;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Failed to refresh weight for TENAM {Tenam}", tenam);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    // Планирует обработку отложенного скана после завершения текущей команды
    private void SchedulePendingScannerProcessing()
    {
        if (string.IsNullOrWhiteSpace(_pendingScannerTenam))
        {
            return;
        }

        // Важно: запускаем ПОСЛЕ возврата из LoadRecordsAsync,
        // чтобы AsyncCommand успел снять флаг "executing"
        _ = Task.Run(async () =>
        {
            await Task.Yield();
            await RunOnUiThreadAsync(TryProcessPendingScannerTenam);
        });
    }

    // Запускает отложенный скан после завершения текущей обработки
    private void TryProcessPendingScannerTenam()
    {
        if (string.IsNullOrWhiteSpace(_pendingScannerTenam))
        {
            return;
        }

        if (IsBusy)
        {
            return;
        }

        var pendingTenam = _pendingScannerTenam;
        _pendingScannerTenam = string.Empty;

        // Для отложенного TENAM пропускаем дедупликацию сканера,
        // иначе повторно принятый код может быть отброшен в окне подавления дублей
        _lastScannedTenam = string.Empty;
        _lastScannedAtUtc = DateTime.MinValue;

        ReceiveTenamFromScanner(pendingTenam);
    }

    // Выполняет обработку TENAM вне UI потока
    private async Task<BoxProcessingResponse> ProcessRequestWithoutUiBlockingAsync(
        BoxProcessingRequest request,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);

        try
        {
            return await Task.Run(
                () => _boxProcessingService.ProcessAsync(request, cancellationToken), 
                cancellationToken
            );
        }
        finally
        {
            _requestGate.Release();
        }
    }

    // Создает новый токен отмены и отменяет предыдущий запрос
    private CancellationToken StartNewLoadCancellation()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();

        return _loadCancellation.Token;
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
        AddNotification(BuildProcessingNotification(response, tenamSnapshot), response.Status is BoxProcessingStatus.Error);

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
    private async Task TryAutoPrintAsync(BoxProcessingResponse response, string tenam, CancellationToken cancellationToken)
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

            var okSheet = await PrintStuffingSheetSilentAsync(
                response,
                tenam,
                settings.StuffingSheetPrinterName,
                settings.StuffingSheetCopies,
                cancellationToken
            );

            if (!okSheet)
            {
                var reason = ResolvePrintFailureReason(settings.StuffingSheetPrinterName);
                StatusMessage = "Не удалось напечатать лист сброса";
                AddNotification($"Лист сброса №{tenam} не отправлен на печать. {reason}", isError: true);
                return;
            }

            AddNotification($"Лист сброса №{tenam} отправлен на печать ({settings.StuffingSheetPrinterName})", isError: false);
        }

        if (settings.PrintEndLabelEnabled && response.Status == BoxProcessingStatus.Success && response.ShouldPrintEndLabels)
        {
            StatusMessage = "Печать торцевой этикетки";

            var okEndLabel = await PrintEndLabelSilentAsync(
                response,
                tenam,
                settings.EndLabelPrinterName,
                settings.EndLabelCopies,
                cancellationToken
            );

            if (!okEndLabel)
            {
                var reason = ResolvePrintFailureReason(settings.EndLabelPrinterName);
                StatusMessage = "Не удалось напечатать торцевую этикетку";
                AddNotification($"Торцевая этикетка №{tenam} не отправлена на печать. {reason}", isError: true);
                return;
            }

            AddNotification($"Торцевая этикетка №{tenam} отправлена на печать ({settings.EndLabelPrinterName})", isError: false);
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
        int copies,
        CancellationToken cancellationToken)
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

        var templateText = await EndLabelTemplateStore.LoadOrCreateAsync(cancellationToken);
        var html = EndLabelHtmlTemplateRenderer.Render(templateText, response, tenam);

        return await SilentHtmlPrinter.PrintHtmlAsync(
            html: html,
            printerName: printerName,
            copies: copies,
            owner: System.Windows.Application.Current?.MainWindow,
            cancellationToken: cancellationToken
        );
    }

    // Печатает лист сброса без отображения окна настроек
    private static async Task<bool> PrintStuffingSheetSilentAsync(
        BoxProcessingResponse response,
        string tenam,
        string printerName,
        int copies,
        CancellationToken cancellationToken)
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
            html = await EmptyPageTemplateStore.LoadOrCreateAsync(cancellationToken);
        }
        else
        {
            var template = await StuffingSheetTemplateStore.LoadOrCreateAsync(cancellationToken);
            html = StuffingSheetHtmlTemplateRenderer.Render(template, response, tenam);
        }

        return await SilentHtmlPrinter.PrintHtmlAsync(
            html: html,
            printerName: printerName,
            copies: copies,
            owner: System.Windows.Application.Current?.MainWindow,
            cancellationToken: cancellationToken
        );
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
            AddNotification("Не удалось запустить COM-сканер: неверная конфигурация", isError: true);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogInformation(exception, "Box scanner is not configured, fallback to keyboard scanner");
            await FailScannerStartAsync();
            AddNotification("COM-сканер не настроен, активирован ввод с клавиатуры", isError: true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to start box scanner, fallback to keyboard scanner");
            await FailScannerStartAsync();
            AddNotification($"Ошибка запуска COM-сканера: {exception.Message}", isError: true);
        }
    }

    // Отвязывает обработчик событий сканера после ошибки запуска
    private async Task FailScannerStartAsync()
    {
        try
        {
            await _boxScanner.StopAsync(CancellationToken.None);
        }
        catch
        {
        }

        if (_isScannerSubscribed)
        {
            _boxScanner.BoxNumberReceived -= OnBoxNumberReceived;
            _isScannerSubscribed = false;
        }
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

    // Преобразует ошибку команды в пользовательский статус
    private void HandleCommandException(Exception exception)
    {
        _ = RunOnUiThreadAsync(() =>
        {
            StatusMessage = exception.Message;
            AddNotification($"Ошибка: {exception.Message}", isError: true);
        });
    }

    private void AddNotification(string message, bool isError)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Notifications.Insert(0, new UiNotification(DateTime.Now, message, isError));

        if (!IsNotificationCenterOpen)
        {
            UnreadNotificationsCount++;
            if (isError)
            {
                HasUnreadErrorNotifications = true;
            }
        }

        if (SelectedNotification is null)
        {
            SelectedNotification = Notifications[0];
        }

        while (Notifications.Count > MaxNotifications)
        {
            Notifications.RemoveAt(Notifications.Count - 1);
        }

        OnPropertyChanged(nameof(FilteredNotifications));
    }


    private static string ResolvePrintFailureReason(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return "Причина: принтер не выбран в настройках печати.";
        }

        if (!PrinterDiscovery.IsPrinterInstalled(printerName))
        {
            return $"Причина: принтер '{printerName}' не найден в системе (проверьте подключение/драйвер).";
        }

        return "Причина: задание отклонено драйвером или очередью печати. Проверьте состояние принтера и очередь заданий.";
    }

    public void ToggleNotificationCenter()
    {
        IsNotificationCenterOpen = !IsNotificationCenterOpen;
    }

    public void OpenNotificationDetails(UiNotification? notification)
    {
        if (notification is null)
        {
            return;
        }

        SelectedNotification = notification;
        IsNotificationCenterOpen = true;
    }

    private void OnNotificationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        OnPropertyChanged(nameof(FilteredNotifications));

        if (SelectedNotification is not null && !Notifications.Contains(SelectedNotification))
        {
            SelectedNotification = Notifications.FirstOrDefault();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Notifications.CollectionChanged -= OnNotificationsCollectionChanged;

        if (_isScannerSubscribed)
        {
            _boxScanner.BoxNumberReceived -= OnBoxNumberReceived;
            _isScannerSubscribed = false;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;

        _scannerGate.Dispose();
        _requestGate.Dispose();
    }

    private static string BuildProcessingNotification(BoxProcessingResponse response, string tenam)
    {
        if (string.IsNullOrWhiteSpace(tenam))
        {
            return response.Message;
        }

        return response.Status switch
        {
            BoxProcessingStatus.Success => $"Короб №{tenam}: {response.Message}",
            BoxProcessingStatus.NotFound => $"Короб №{tenam}: данные не найдены",
            BoxProcessingStatus.NeedWeight => $"Короб №{tenam}: требуется ввод веса",
            _ => $"Короб №{tenam}: {response.Message}"
        };
    }
}
