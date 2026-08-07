using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.BoxProcessing;
using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.Templates;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// Основная модель представления экрана обработки коробов
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan ScannerDeduplicationWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ScaleWeightPollingInterval = TimeSpan.FromSeconds(2);
    private const int MaxNotifications = 50;

    private readonly IBoxProcessingService _boxProcessingService;
    private readonly IBoxWeightService _boxWeightService;
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
    private bool _nextRequestTriggeredByScanner;
    private WorkMode _currentWorkMode = WorkMode.Manual;
    private int _automaticDrainRequestsRemaining;

    private EndLabelTemplatePreviewWindow? _endLabelPreviewWindow;
    private StuffingSheetTemplatePreviewWindow? _stuffingSheetPreviewWindow;

    private string _tenam = string.Empty;
    private string _statusMessage = string.Empty;
    private BoxProcessingStatus? _lastProcessingStatus;
    private bool _isBusy;
    private OracleConnectionState _oracleConnectionState = OracleConnectionState.Unknown;
    private string _oracleConnectionStatusDetail = "Запрос к базе данных в текущем запуске ещё не выполнялся.";
    private string _currentOracleQueryTenam = string.Empty;
    private bool _isScannerSubscribed;

    private string _lastProcessedTenam = string.Empty;
    private string _lastScannedTenam = string.Empty;
    private string _pendingScannerTenam = string.Empty;

    private DateTime _lastScannedAtUtc;

    private bool _isNotificationCenterOpen;
    private UiNotification? _selectedNotification;
    private int _notificationTabIndex;
    private int _unreadNotificationsCount;
    private int _unreadErrorNotificationsCount;
    private int _unreadWarningNotificationsCount;
    private int _unreadSuccessNotificationsCount;
    private bool _disposed;

    private bool _canRequestManualWeight;
    private bool _isManualScanAutoPrintEndLabelEnabled;
    private string _tenamAwaitingWeight = string.Empty;

    /// <summary>
    /// Создает модель представления главного экрана
    /// </summary>
    public MainViewModel(
        IBoxProcessingService boxProcessingService,
        IBoxWeightService boxWeightService,
        IBoxScanner boxScanner,
        ILogger<MainViewModel> logger)
    {
        _boxProcessingService = boxProcessingService ?? throw new ArgumentNullException(nameof(boxProcessingService));
        _boxWeightService = boxWeightService ?? throw new ArgumentNullException(nameof(boxWeightService));
        _boxScanner = boxScanner ?? throw new ArgumentNullException(nameof(boxScanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uiContext = SynchronizationContext.Current;

        Records = new ObservableCollection<LabelRecord>();
        Notifications = new ObservableCollection<UiNotification>();
        Notifications.CollectionChanged += OnNotificationsCollectionChanged;
        FilteredNotificationsView = CollectionViewSource.GetDefaultView(Notifications);
        FilteredNotificationsView.Filter = FilterNotificationByCurrentTab;

        LoadRecordsCommand = new AsyncCommand(LoadRecordsAsync, CanLoadRecords, HandleCommandException);
        OpenEndLabelPreviewCommand = new AsyncCommand(OpenEndLabelPreviewAsync, CanOpenEndLabelPreview, HandleCommandException);
        OpenStuffingSheetPreviewCommand = new AsyncCommand(OpenStuffingSheetPreviewAsync, CanOpenStuffingSheetPreview, HandleCommandException);
        RequestManualWeightCommand = new AsyncCommand(RequestManualWeightAgainAsync, CanRequestManualWeightAgain, HandleCommandException);
        SwitchToAutomaticModeCommand = new RelayCommand(() => CurrentWorkMode = WorkMode.Automatic);
        SwitchToManualModeCommand = new RelayCommand(() => CurrentWorkMode = WorkMode.Manual);

        var settings = PrintSettingsStore.LoadOrDefault();
        _currentWorkMode = settings?.WorkMode ?? WorkMode.Manual;
        _isManualScanAutoPrintEndLabelEnabled = settings?.ManualScanAutoPrintEndLabelEnabled ?? false;

        StatusMessage = "Введите или отсканируйте TENAM и нажмите Enter";

        _ = InitializeScannerAsync();
    }

    /// <summary>
    /// Коллекция записей для отображения в таблице
    /// </summary>
    public ObservableCollection<LabelRecord> Records { get; }

    /// <summary>
    /// Коллекция уведомлений пользователя
    /// </summary>
    public ObservableCollection<UiNotification> Notifications { get; }

    /// <summary>
    /// Отфильтрованное представление уведомлений
    /// </summary>
    public ICollectionView FilteredNotificationsView { get; }

    /// <summary>
    /// Выбранное уведомление
    /// </summary>
    public UiNotification? SelectedNotification
    {
        get => _selectedNotification;
        set => SetProperty(ref _selectedNotification, value);
    }

    /// <summary>
    /// Возвращает признак доступности повторного ввода веса
    /// </summary>
    public bool CanRequestManualWeight
    {
        get => _canRequestManualWeight;
        private set
        {
            if (SetProperty(ref _canRequestManualWeight, value))
            {
                RequestManualWeightCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Возвращает или задает признак открытого центра уведомлений
    /// </summary>
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
                EnsureSelectedNotificationMatchesCurrentTab();
            }
        }
    }

    /// <summary>
    /// Индекс выбранного фильтра уведомлений. По умолчанию панель показывает
    /// только проблемы, при этом успешные события остаются доступны через фильтр.
    /// </summary>
    public int NotificationTabIndex
    {
        get => _notificationTabIndex;
        set
        {
            if (!SetProperty(ref _notificationTabIndex, value))
            {
                return;
            }

            RefreshFilteredNotifications();
            EnsureSelectedNotificationMatchesCurrentTab();
        }
    }

    /// <summary>
    /// Количество непрочитанных уведомлений
    /// </summary>
    public int UnreadNotificationsCount
    {
        get => _unreadNotificationsCount;
        private set => SetProperty(ref _unreadNotificationsCount, value);
    }

    /// <summary>
    /// Количество непрочитанных ошибок
    /// </summary>
    public int UnreadErrorNotificationsCount
    {
        get => _unreadErrorNotificationsCount;
        private set => SetProperty(ref _unreadErrorNotificationsCount, value);
    }

    /// <summary>
    /// Количество непрочитанных предупреждений
    /// </summary>
    public int UnreadWarningNotificationsCount
    {
        get => _unreadWarningNotificationsCount;
        private set => SetProperty(ref _unreadWarningNotificationsCount, value);
    }

    /// <summary>
    /// Количество непрочитанных успешных уведомлений
    /// </summary>
    public int UnreadSuccessNotificationsCount
    {
        get => _unreadSuccessNotificationsCount;
        private set => SetProperty(ref _unreadSuccessNotificationsCount, value);
    }

    /// <summary>
    /// Количество непрочитанных проблем для shell-indicator. Отдельное свойство
    /// не заставляет представление предполагать, что success-события всегда
    /// должны входить в операторский счётчик.
    /// </summary>
    public int UnreadProblemNotificationsCount =>
        UnreadErrorNotificationsCount + UnreadWarningNotificationsCount;

    /// <summary>
    /// Возвращает признак непрочитанных ошибок
    /// </summary>
    public bool HasUnreadErrorNotifications => UnreadErrorNotificationsCount > 0;

    /// <summary>
    /// Возвращает признак непрочитанных предупреждений
    /// </summary>
    public bool HasUnreadWarningNotifications => UnreadWarningNotificationsCount > 0;

    /// <summary>
    /// Возвращает признак непрочитанных успешных уведомлений
    /// </summary>
    public bool HasUnreadSuccessNotifications => UnreadSuccessNotificationsCount > 0;

    /// <summary>
    /// Команда повторного запроса ручного веса
    /// </summary>
    public AsyncCommand RequestManualWeightCommand { get; }

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
    /// Запрашивает переключение оборудования в автоматический режим.
    /// </summary>
    public RelayCommand SwitchToAutomaticModeCommand { get; }

    /// <summary>
    /// Запрашивает переключение оборудования в ручной режим.
    /// </summary>
    public RelayCommand SwitchToManualModeCommand { get; }

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
    /// Структурный результат последней завершённой обработки. Используется только
    /// представлением для выбора success/not-found/error state без разбора текста.
    /// </summary>
    public BoxProcessingStatus? LastProcessingStatus
    {
        get => _lastProcessingStatus;
        private set => SetProperty(ref _lastProcessingStatus, value);
    }

    /// <summary>
    /// Runtime-состояние Oracle, подтверждённое реальными запросами текущего запуска.
    /// Бизнес-результаты обработки не интерпретируются как ошибки соединения.
    /// </summary>
    public OracleConnectionState OracleConnectionState
    {
        get => _oracleConnectionState;
        private set => SetProperty(ref _oracleConnectionState, value);
    }

    /// <summary>
    /// Краткое безопасное пояснение runtime-состояния Oracle.
    /// Не содержит текста исключения или строки подключения.
    /// </summary>
    public string OracleConnectionStatusDetail
    {
        get => _oracleConnectionStatusDetail;
        private set => SetProperty(ref _oracleConnectionStatusDetail, value);
    }

    /// <summary>
    /// TENAM запроса, который сейчас проверяет доступность Oracle.
    /// Пуст после завершения или отмены операции.
    /// </summary>
    public string CurrentOracleQueryTenam
    {
        get => _currentOracleQueryTenam;
        private set => SetProperty(ref _currentOracleQueryTenam, value);
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
                RequestManualWeightCommand.RaiseCanExecuteChanged();
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
            var previousMode = _currentWorkMode;

            if (!SetProperty(ref _currentWorkMode, value))
            {
                return;
            }

            if (previousMode == WorkMode.Automatic && value == WorkMode.Manual)
            {
                _automaticDrainRequestsRemaining = 1;
                AddNotification("Следующий считанный короб будет обработан как автоматический", NotificationCategory.Warning);
            }

            OnPropertyChanged(nameof(IsAutomaticMode));
            _ = SaveWorkModeAsync(value);
        }
    }

    /// <summary>
    /// Включает печать торцевой этикетки сразу после сканирования в ручном режиме
    /// </summary>
    public bool IsManualScanAutoPrintEndLabelEnabled
    {
        get => _isManualScanAutoPrintEndLabelEnabled;
        set
        {
            if (!SetProperty(ref _isManualScanAutoPrintEndLabelEnabled, value))
            {
                return;
            }

            _ = SaveManualScanAutoPrintModeAsync(value);
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

            _nextRequestMode = ResolveNextRequestMode();
            _nextRequestTriggeredByScanner = true;
            Tenam = digitsOnly;

            if (LoadRecordsCommand.CanExecute(null))
            {
                LoadRecordsCommand.Execute(null);
            }
        });
    }

    private bool CanRequestManualWeightAgain()
    {
        if (IsBusy)
        {
            return false;
        }

        return CanRequestManualWeight && !string.IsNullOrWhiteSpace(_tenamAwaitingWeight);
    }

    private async Task RequestManualWeightAgainAsync()
    {
        if (string.IsNullOrWhiteSpace(_tenamAwaitingWeight))
        {
            return;
        }

        var tenamSnapshot = _tenamAwaitingWeight;
        var cancellationToken = StartNewLoadCancellation();

        IsBusy = true;
        StatusMessage = "Проверка веса";

        try
        {
            var response = await ProcessRequestWithoutUiBlockingAsync(
                BuildRequest(tenamSnapshot, WorkMode.Manual),
                "RequestManualWeightAgain",
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (response.Status != BoxProcessingStatus.NeedWeight)
            {
                ApplyResponseState(response, tenamSnapshot);

                if (response.Status == BoxProcessingStatus.Success)
                {
                    _tenamAwaitingWeight = string.Empty;
                    CanRequestManualWeight = false;
                }

                return;
            }

            IsBusy = false;
            StatusMessage = "Ожидание ввода веса";

            var updatedResponse = await RequestManualWeightAsync(response, tenamSnapshot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ApplyResponseState(updatedResponse, tenamSnapshot);

            if (updatedResponse.Status == BoxProcessingStatus.Success)
            {
                _tenamAwaitingWeight = string.Empty;
                CanRequestManualWeight = false;
            }
            else
            {
                _tenamAwaitingWeight = tenamSnapshot;
                CanRequestManualWeight = true;
                StatusMessage = "Нет веса в БД. Поставьте короб на весы или нажмите Ввести вес";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Операция отменена";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to request manual weight again for TENAM {Tenam}", tenamSnapshot);
            LastProcessingStatus = BoxProcessingStatus.Error;
            StatusMessage = "Не удалось повторно запросить ввод веса";
            AddNotification(
                $"Не удалось повторно получить данные для короба №{tenamSnapshot} из базы данных.",
                NotificationCategory.Error);
        }
        finally
        {
            IsBusy = false;
            RequestManualWeightCommand.RaiseCanExecuteChanged();
        }
    }

    private WorkMode ResolveNextRequestMode()
    {
        if (CurrentWorkMode == WorkMode.Automatic)
        {
            return WorkMode.Automatic;
        }

        if (_automaticDrainRequestsRemaining <= 0)
        {
            return WorkMode.Manual;
        }

        _automaticDrainRequestsRemaining--;
        return WorkMode.Automatic;
    }

    // Сохраняет выбранный режим работы в настройках
    private async Task SaveWorkModeAsync(WorkMode workMode)
    {
        try
        {
            await PrintSettingsStore.UpdateAsync(
                settings =>
                {
                    settings.WorkMode = workMode;
                    return settings;
                },
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to persist selected work mode");
        }
    }

    // Сохраняет признак полуавтоматической печати торцевой этикетки
    private async Task SaveManualScanAutoPrintModeAsync(bool enabled)
    {
        try
        {
            await PrintSettingsStore.UpdateAsync(
                settings =>
                {
                    settings.ManualScanAutoPrintEndLabelEnabled = enabled;
                    return settings;
                },
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to persist semi-automatic end label setting");
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
        var requestTriggeredByScanner = _nextRequestTriggeredByScanner;
        _nextRequestMode = WorkMode.Manual;
        _nextRequestTriggeredByScanner = false;

        var tenamSnapshot = Tenam?.Trim() ?? string.Empty;
        var cancellationToken = StartNewLoadCancellation();

        IsBusy = true;
        StatusMessage = "Загрузка";
        LastProcessingStatus = null;
        Records.Clear();
        Tenam = string.Empty;

        // Сбрасываем возможность повторного ввода веса для предыдущего короба
        _tenamAwaitingWeight = string.Empty;
        CanRequestManualWeight = false;

        try
        {
            var request = BuildRequest(tenamSnapshot, requestMode);

            await WaitForUiToRenderAsync();

            var response = await ProcessRequestWithoutUiBlockingAsync(request, "LoadRecords", cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var requiredManualWeight = response.Status == BoxProcessingStatus.NeedWeight;
            if (requiredManualWeight)
            {
                var shouldRequestManualWeight = response.PrintPlan.IsEmpty;

                if (shouldRequestManualWeight)
                {
                    _tenamAwaitingWeight = tenamSnapshot;
                    CanRequestManualWeight = true;

                    IsBusy = false;
                    StatusMessage = "Нет веса в БД. Поставьте короб на весы или нажмите «Ввести вес».";

                    response = await RequestManualWeightAsync(response, tenamSnapshot, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (response.Status == BoxProcessingStatus.Success)
                    {
                        _tenamAwaitingWeight = string.Empty;
                        CanRequestManualWeight = false;
                        IsBusy = true;
                    }
                }
                else
                {
                    AddNotification(
                        $"Короб №{tenamSnapshot}: вес не найден в БД в автоматическом режиме и отключены весы.",
                        NotificationCategory.Warning);
                }
            }

            ApplyResponseState(response, tenamSnapshot);

            var shouldAutoPrintInSemiAutomaticMode = requestMode == WorkMode.Manual
                                                     && requestTriggeredByScanner
                                                     && IsManualScanAutoPrintEndLabelEnabled
                                                     && response.Status == BoxProcessingStatus.Success
                                                     && response.PrintPlan.PrintEndLabels;

            if (requestMode == WorkMode.Automatic || (requiredManualWeight && response.Status == BoxProcessingStatus.Success))
            {
                await TryAutoPrintAsync(response, tenamSnapshot, cancellationToken, printDropSheet: true, printEndLabel: true);
            }
            else if (shouldAutoPrintInSemiAutomaticMode)
            {
                await TryAutoPrintAsync(response, tenamSnapshot, cancellationToken, printDropSheet: false, printEndLabel: true);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Операция отменена";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load records");

            ApplyFailedLoadState("Не удалось получить данные из базы данных.");
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
        using var weightMonitorTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var scaleResponseTask = Task.Run(
            () => WaitForWeightFromScalesAsync(tenam, weightMonitorTokenSource.Token),
            CancellationToken.None);

        var dialog = new ManualWeightInputWindow(tenam)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        _ = Task.Run(async () =>
        {
            var scaleWeightValue = await scaleResponseTask.ConfigureAwait(false);
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

        weightMonitorTokenSource.Cancel();

        decimal? enteredWeight = accepted ? dialog.EnteredWeight : null;
        decimal? scaleWeight = accepted ? dialog.ScaleWeight : null;

        if (scaleWeight.HasValue)
        {
            var refreshed = await ProcessRequestWithoutUiBlockingAsync(
                BuildRequest(tenam, WorkMode.Manual),
                "ScaleWeightRefresh",
                cancellationToken);

            if (refreshed.Status == BoxProcessingStatus.Success && refreshed.Weight.HasValue && refreshed.Weight > 0)
            {
                var printSettings = PrintSettingsStore.LoadOrDefault() ?? new PrintSettings();

                return refreshed with
                {
                    Message = "Вес получен с весов",
                    PrintPlan = new PrintPlan(
                        PrintDropSheet: printSettings.PrintStuffingSheetEnabled,
                        PrintEmptyDropSheet: false,
                        PrintEndLabels: printSettings.PrintEndLabelEnabled)
                };
            }

            return response with
            {
                Message = "Вес с весов получен, но данные не обновились. Повторите сканирование."
            };
        }

        if (!enteredWeight.HasValue)
        {
            return response with
            {
                Message = "Ввод веса отменен. Нажмите «Ввести вес», чтобы повторить."
            };
        }

        var manualWeight = Math.Round(enteredWeight.Value, 3, MidpointRounding.AwayFromZero);

        var weightUpdateResult = await _boxWeightService.UpdateWeightAsync(
            tenam,
            manualWeight,
            cancellationToken);

        if (!weightUpdateResult.IsSuccess)
        {
            AddNotification(
                $"Не удалось сохранить вес для короба №{tenam} в БД. {weightUpdateResult.Message}",
                NotificationCategory.Error);

            return response with { Message = weightUpdateResult.Message };
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
            PrintPlan = new PrintPlan(
                PrintDropSheet: settings.PrintStuffingSheetEnabled,
                PrintEmptyDropSheet: false,
                PrintEndLabels: settings.PrintEndLabelEnabled)
        };
    }

    private async Task<decimal?> WaitForWeightFromScalesAsync(string tenam, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ScaleWeightPollingInterval, cancellationToken).ConfigureAwait(false);

                var currentResponse = await ProcessRequestWithoutUiBlockingAsync(
                    BuildRequest(tenam, WorkMode.Manual),
                    "ScaleWeightPolling",
                    cancellationToken).ConfigureAwait(false);

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
    private static async Task WaitForUiToRenderAsync()
    {
        if (System.Windows.Application.Current?.Dispatcher is null)
        {
            await Task.Yield();
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(
            static () => { },
            System.Windows.Threading.DispatcherPriority.Render,
            CancellationToken.None);
    }

    private async Task<BoxProcessingResponse> ProcessRequestWithoutUiBlockingAsync(
        BoxProcessingRequest request,
        string origin,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);

        var previousState = OracleConnectionState;
        var previousDetail = OracleConnectionStatusDetail;

        try
        {
            await SetOracleConnectionStateAsync(
                OracleConnectionState.Checking,
                "Выполняется запрос к базе данных.",
                request.Tenam);

            _logger.LogDebug(
                "Start processing TENAM {Tenam} from {Origin} in mode {Mode}",
                request.Tenam,
                origin,
                request.Mode);

            var response = await Task.Run(
                () => _boxProcessingService.ProcessAsync(request, cancellationToken),
                CancellationToken.None);

            _logger.LogDebug(
                "Completed processing TENAM {Tenam} from {Origin} with status {Status}",
                request.Tenam,
                origin,
                response.Status);

            // BoxProcessingStatus.Error также используется для бизнес-ошибок
            // (например, конфликта веса). Сам факт возвращённого ответа означает,
            // что запрос к источнику данных завершился без исключения доступа.
            await SetOracleConnectionStateAsync(
                OracleConnectionState.Connected,
                "Последний запрос к базе данных выполнен успешно.");

            return response;
        }
        catch (OperationCanceledException)
        {
            await SetOracleConnectionStateAsync(previousState, previousDetail);
            throw;
        }
        catch (Exception)
        {
            await SetOracleConnectionStateAsync(
                OracleConnectionState.Error,
                "Не удалось получить данные из базы данных.");
            throw;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private Task SetOracleConnectionStateAsync(
        OracleConnectionState state,
        string detail,
        string currentTenam = "") =>
        RunOnUiThreadAsync(() =>
        {
            CurrentOracleQueryTenam = state == OracleConnectionState.Checking
                ? currentTenam
                : string.Empty;
            OracleConnectionStatusDetail = detail;
            OracleConnectionState = state;
        });

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
            ShouldPrintStuffingSheet: settings.PrintStuffingSheetEnabled,
            UseScales: settings.UseScales
        );
    }

    // Применяет результат обработки к состоянию экрана
    private void ApplyResponseState(BoxProcessingResponse response, string tenamSnapshot)
    {
        LastProcessingStatus = response.Status;

        foreach (var record in response.Records)
        {
            Records.Add(record);
        }

        StatusMessage = response.Message;
        AddNotification(BuildProcessingNotification(response, tenamSnapshot), ResolveProcessingNotificationCategory(response));

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
        LastProcessingStatus = BoxProcessingStatus.Error;
        StatusMessage = errorMessage;
        _lastLoadedResponse = null;
        _lastLoadedTenam = string.Empty;
        _lastSuccessfulResponse = null;
        _lastSuccessfulTenam = string.Empty;
        LastProcessedTenam = string.Empty;

        _tenamAwaitingWeight = string.Empty;
        CanRequestManualWeight = false;

        AddNotification($"Ошибка: {errorMessage}", NotificationCategory.Error);

        OpenEndLabelPreviewCommand.RaiseCanExecuteChanged();
        OpenStuffingSheetPreviewCommand.RaiseCanExecuteChanged();
        RequestManualWeightCommand.RaiseCanExecuteChanged();
    }

    // Выполняет бесшумную автопечать после успешной обработки
    private async Task TryAutoPrintAsync(
        BoxProcessingResponse response,
        string tenam,
        CancellationToken cancellationToken,
        bool printDropSheet,
        bool printEndLabel)
    {
        // В fast-режиме никаких попапов, только статус
        var settings = PrintSettingsStore.LoadOrDefault();

        if (settings is null || !settings.IsComplete)
        {
            StatusMessage = "Не настроены принтеры для быстрой печати";
            return;
        }

        if ((!settings.PrintEndLabelEnabled || !printEndLabel) && (!settings.PrintStuffingSheetEnabled || !printDropSheet))
        {
            StatusMessage = "Автопечать отключена в настройках";
            return;
        }

        // Сначала печатаем лист сброса, затем торцевую этикетку
        if (printDropSheet
            && settings.PrintStuffingSheetEnabled
            && (response.PrintPlan.PrintDropSheet || response.PrintPlan.PrintEmptyDropSheet))
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
                AddNotification($"Лист сброса №{tenam} не отправлен на печать. {reason}", NotificationCategory.Error);
                return;
            }

            AddNotification($"Лист сброса №{tenam} отправлен на печать ({settings.StuffingSheetPrinterName})", NotificationCategory.Success);
        }

        if (printEndLabel
            && settings.PrintEndLabelEnabled
            && response.Status == BoxProcessingStatus.Success
            && response.PrintPlan.PrintEndLabels)
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
                AddNotification($"Торцевая этикетка №{tenam} не отправлена на печать. {reason}", NotificationCategory.Error);
                return;
            }

            AddNotification($"Торцевая этикетка №{tenam} отправлена на печать ({settings.EndLabelPrinterName})", NotificationCategory.Success);
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
            AddNotification("Не удалось запустить COM-сканер: неверная конфигурация", NotificationCategory.Error);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogInformation(exception, "Box scanner is not configured, fallback to keyboard scanner");
            await FailScannerStartAsync();
            AddNotification("COM-сканер не настроен, активирован ввод с клавиатуры", NotificationCategory.Error);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to start box scanner, fallback to keyboard scanner");
            await FailScannerStartAsync();
            AddNotification($"Ошибка запуска COM-сканера: {exception.Message}", NotificationCategory.Error);
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
            AddNotification($"Ошибка: {exception.Message}", NotificationCategory.Error);
        });
    }

    private void AddNotification(string message, NotificationCategory category)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var notification = new UiNotification(DateTime.Now, message, category);

        Notifications.Insert(0, notification);
        IncreaseUnreadCounter(category);

        EnsureSelectedNotificationMatchesCurrentTab();

        while (Notifications.Count > MaxNotifications)
        {
            var removedNotification = Notifications[^1];

            if (removedNotification.IsUnread)
            {
                DecreaseUnreadCounter(removedNotification.Category);
            }

            Notifications.RemoveAt(Notifications.Count - 1);
        }

        RefreshFilteredNotifications();
    }

    /// <summary>
    /// Помечает уведомление как прочитанное
    /// </summary>
    public void MarkNotificationAsRead(UiNotification? notification)
    {
        if (notification is null || notification.IsRead)
        {
            return;
        }

        notification.MarkAsRead();
        DecreaseUnreadCounter(notification.Category);
        RefreshFilteredNotifications();
    }

    private void IncreaseUnreadCounter(NotificationCategory category)
    {
        UnreadNotificationsCount++;

        switch (category)
        {
            case NotificationCategory.Error:
                UnreadErrorNotificationsCount++;
                break;

            case NotificationCategory.Warning:
                UnreadWarningNotificationsCount++;
                break;

            default:
                UnreadSuccessNotificationsCount++;
                break;
        }

        NotifyUnreadNotificationStateChanged();
    }

    private void DecreaseUnreadCounter(NotificationCategory category)
    {
        UnreadNotificationsCount = Math.Max(0, UnreadNotificationsCount - 1);

        switch (category)
        {
            case NotificationCategory.Error:
                UnreadErrorNotificationsCount = Math.Max(0, UnreadErrorNotificationsCount - 1);
                break;

            case NotificationCategory.Warning:
                UnreadWarningNotificationsCount = Math.Max(0, UnreadWarningNotificationsCount - 1);
                break;

            default:
                UnreadSuccessNotificationsCount = Math.Max(0, UnreadSuccessNotificationsCount - 1);
                break;
        }

        NotifyUnreadNotificationStateChanged();
    }

    private void NotifyUnreadNotificationStateChanged()
    {
        OnPropertyChanged(nameof(HasUnreadErrorNotifications));
        OnPropertyChanged(nameof(HasUnreadWarningNotifications));
        OnPropertyChanged(nameof(HasUnreadSuccessNotifications));
        OnPropertyChanged(nameof(UnreadProblemNotificationsCount));
    }

    private void RefreshFilteredNotifications()
    {
        FilteredNotificationsView.Refresh();
        OnPropertyChanged(nameof(FilteredNotificationsView));
    }

    private void EnsureSelectedNotificationMatchesCurrentTab()
    {
        if (FilteredNotificationsView.IsEmpty)
        {
            SelectedNotification = null;
            return;
        }

        if (SelectedNotification is UiNotification current && FilterNotificationByCurrentTab(current))
        {
            return;
        }

        SelectedNotification = FilteredNotificationsView.Cast<UiNotification>().FirstOrDefault();
    }

    private bool FilterNotificationByCurrentTab(object item)
    {
        if (item is not UiNotification notification)
        {
            return false;
        }

        return NotificationTabIndex switch
        {
            0 => notification.Category is NotificationCategory.Error or NotificationCategory.Warning,
            2 => notification.Category == NotificationCategory.Error,
            3 => notification.Category == NotificationCategory.Warning,
            4 => notification.Category == NotificationCategory.Success,
            _ => true
        };
    }

    private static NotificationCategory ResolveProcessingNotificationCategory(BoxProcessingResponse response)
    {
        if (response.Status == BoxProcessingStatus.Error)
        {
            return NotificationCategory.Error;
        }

        if (response.Status == BoxProcessingStatus.NeedWeight)
        {
            return NotificationCategory.Warning;
        }

        return NotificationCategory.Success;
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

    /// <summary>
    /// Переключает отображение центра уведомлений
    /// </summary>
    public void ToggleNotificationCenter()
    {
        IsNotificationCenterOpen = !IsNotificationCenterOpen;
    }

    /// <summary>
    /// Открывает детали уведомления
    /// </summary>
    public void OpenNotificationDetails(UiNotification? notification)
    {
        if (notification is null)
        {
            return;
        }

        SelectedNotification = notification;
        IsNotificationCenterOpen = true;
        MarkNotificationAsRead(notification);
    }

    private void OnNotificationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        RefreshFilteredNotifications();

        EnsureSelectedNotificationMatchesCurrentTab();
    }

    /// <summary>
    /// Освобождает ресурсы модели представления
    /// </summary>
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
