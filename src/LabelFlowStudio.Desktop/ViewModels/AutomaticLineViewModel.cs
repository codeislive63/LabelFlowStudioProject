using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Devices.BoxScanner;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// Визуальное состояние мониторингового экрана автоматической линии.
/// Не запускает обработку и не изменяет очередь сканов: все данные проецируются
/// из существующей рабочей модели и текущей конфигурации оборудования.
/// </summary>
public sealed class AutomaticLineViewModel : ViewModelBase, IDisposable
{
    private const int RecentEventLimit = 5;
    private static readonly TimeSpan SuccessStateDuration = TimeSpan.FromSeconds(6);

    private readonly Func<AutomaticLineEquipmentSnapshot> _equipmentSnapshotProvider;
    private readonly Func<DateTime> _clock;
    private IReadOnlyList<AutomaticLineEvent> _recentEvents = [];
    private AutomaticLineEvent? _currentLineEvent;
    private DateTime? _successStateExpiresAt;
    private bool _hasAutomaticActivity;
    private bool _isScannerRunning;
    private bool _isPrinterInstalled;
    private bool _isScalesEnabled;
    private bool _hasEquipmentSnapshot;
    private int _equipmentRefreshPending;
    private bool _disposed;

    public AutomaticLineViewModel(MainViewModel work, IBoxScanner boxScanner)
        : this(
            work,
            CreateEquipmentSnapshotProvider(
                boxScanner ?? throw new ArgumentNullException(nameof(boxScanner))))
    {
    }

    internal AutomaticLineViewModel(
        MainViewModel work,
        Func<AutomaticLineEquipmentSnapshot> equipmentSnapshotProvider,
        Func<DateTime>? clock = null)
    {
        Work = work ?? throw new ArgumentNullException(nameof(work));
        _equipmentSnapshotProvider = equipmentSnapshotProvider
            ?? throw new ArgumentNullException(nameof(equipmentSnapshotProvider));
        _clock = clock ?? (() => DateTime.Now);

        Work.PropertyChanged += OnWorkPropertyChanged;
        Work.Notifications.CollectionChanged += OnNotificationsCollectionChanged;

        RefreshRecentEvents();
    }

    public MainViewModel Work { get; }

    public AutomaticLineState LineState
    {
        get
        {
            if (Work.CurrentWorkMode != WorkMode.Automatic)
            {
                return AutomaticLineState.Disabled;
            }

            var automaticRequestIsBusy =
                Work.IsBusy && Work.ActiveRequestMode == WorkMode.Automatic;

            var automaticStatusMessage =
                Work.ActiveRequestMode == WorkMode.Manual
                    ? null
                    : _hasAutomaticActivity ? Work.StatusMessage : null;

            var projectedState = ResolveLineState(
                automaticRequestIsBusy,
                automaticStatusMessage,
                _currentLineEvent,
                IsSuccessStateVisible);

            if (IsOracleStatusError && !Work.IsBusy)
            {
                return AutomaticLineState.Error;
            }

            if (!HasEquipmentSnapshot && projectedState == AutomaticLineState.Idle)
            {
                return AutomaticLineState.Initializing;
            }

            // The scanner is the only device whose runtime state is currently known.
            // Do not present a healthy idle line after that real check reports it stopped.
            return HasEquipmentSnapshot
                && !IsScannerRunning
                && !Work.IsBusy
                && projectedState is AutomaticLineState.Idle or AutomaticLineState.Success
                    ? AutomaticLineState.Error
                    : projectedState;
        }
    }

    public string LineHeadline => LineState switch
    {
        AutomaticLineState.Disabled => "Автоматическая обработка выключена",
        AutomaticLineState.Initializing => "Инициализация линии",
        AutomaticLineState.Idle => "Линия работает",
        AutomaticLineState.Loading => "Получение данных",
        AutomaticLineState.Processing => "Обработка короба",
        AutomaticLineState.Printing => ResolveStatusMessage("Печать"),
        AutomaticLineState.Success => "Короб обработан",
        AutomaticLineState.Warning => "Требуется внимание",
        AutomaticLineState.Error when HasEquipmentSnapshot && !IsScannerRunning => "Линия не готова",
        AutomaticLineState.Error => "Ошибка линии",
        _ => "Линия работает"
    };

    public string LineSubtitle => LineState switch
    {
        AutomaticLineState.Disabled => "Включить её можно в настройках",
        AutomaticLineState.Initializing => "Проверка состояния сканера",
        AutomaticLineState.Idle => "Ожидание следующего короба",
        AutomaticLineState.Success => LastBoxActionText == NoDataText
            ? "Ожидание следующего короба"
            : LastBoxActionText,
        AutomaticLineState.Loading => string.IsNullOrWhiteSpace(Work.CurrentOracleQueryTenam)
            ? "Получение данных коробки…"
            : $"Получение данных коробки {Work.CurrentOracleQueryTenam}…",
        AutomaticLineState.Processing => ResolveStatusMessage("Короб обрабатывается"),
        AutomaticLineState.Printing => ResolveStatusMessage("Документ отправляется на печать"),
        AutomaticLineState.Error when HasEquipmentSnapshot && !IsScannerRunning =>
            "Сканер не готов",
        AutomaticLineState.Error when IsOracleStatusError => OracleStatusToolTip,
        AutomaticLineState.Warning or AutomaticLineState.Error =>
            ResolveLineIssueMessage(),
        _ => "Ожидание следующего короба"
    };

    public bool IsDisabled => LineState == AutomaticLineState.Disabled;

    public bool IsIdle => LineState == AutomaticLineState.Idle;

    public bool IsInitializing => LineState == AutomaticLineState.Initializing;

    public bool IsLoading => LineState == AutomaticLineState.Loading;

    public bool IsProcessing => LineState == AutomaticLineState.Processing;

    public bool IsPrinting => LineState == AutomaticLineState.Printing;

    public bool IsSuccess => LineState == AutomaticLineState.Success;

    public bool IsWarning => LineState == AutomaticLineState.Warning;

    public bool IsError => LineState == AutomaticLineState.Error;

    public bool HasLastBox => !string.IsNullOrWhiteSpace(Work.LastProcessedTenam);

    public string LastBoxTenamText => string.IsNullOrWhiteSpace(Work.LastProcessedTenam)
        ? NoDataValue
        : Work.LastProcessedTenam;

    public string LastBoxTimeText => LastBoxNotification is { } notification
        ? notification.Timestamp.ToString("HH:mm:ss")
        : NoDataValue;

    public string LastBoxResultText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Work.LastProcessedTenam))
            {
                return NoDataText;
            }

            return LastBoxNotification is { } notification
                ? ResolveEventSeverity(notification) switch
                {
                    AutomaticLineEventSeverity.Error => "Ошибка",
                    AutomaticLineEventSeverity.Warning => "Предупреждение",
                    AutomaticLineEventSeverity.Success => "Успешно",
                    _ => "Успешно"
                }
                : "Успешно";
        }
    }

    public string LastBoxActionText => LastBoxNotification is { } notification
        ? ResolveCompactAction(notification.Message)
        : NoDataText;

    public bool IsLastBoxSuccess => HasLastBox
        && (LastBoxNotification is null
            || ResolveEventSeverity(LastBoxNotification) == AutomaticLineEventSeverity.Success);

    public bool IsLastBoxWarning => HasLastBox
        && LastBoxNotification is { } notification
        && ResolveEventSeverity(notification) == AutomaticLineEventSeverity.Warning;

    public bool IsLastBoxError => HasLastBox
        && LastBoxNotification is { } notification
        && ResolveEventSeverity(notification) == AutomaticLineEventSeverity.Error;

    public IReadOnlyList<AutomaticLineEvent> RecentEvents
    {
        get => _recentEvents;
        private set => SetProperty(ref _recentEvents, value);
    }

    public bool HasRecentEvents => RecentEvents.Count > 0;

    public bool IsScannerRunning
    {
        get => _isScannerRunning;
        private set => SetProperty(ref _isScannerRunning, value);
    }

    public bool HasEquipmentSnapshot
    {
        get => _hasEquipmentSnapshot;
        private set => SetProperty(ref _hasEquipmentSnapshot, value);
    }

    public string ScannerStatusText => !HasEquipmentSnapshot
        ? "Проверка…"
        : IsScannerRunning ? "Работает" : "Не готов";

    public bool IsPrinterInstalled
    {
        get => _isPrinterInstalled;
        private set => SetProperty(ref _isPrinterInstalled, value);
    }

    public string PrinterStatusText => !HasEquipmentSnapshot
        ? "Проверка…"
        : IsPrinterInstalled ? "Установлен" : "Не найден";

    public bool IsScalesEnabled
    {
        get => _isScalesEnabled;
        private set => SetProperty(ref _isScalesEnabled, value);
    }

    public string ScalesStatusText => !HasEquipmentSnapshot
        ? "Проверка…"
        : IsScalesEnabled ? "Включены в настройках" : "Отключены";

    public OracleConnectionState OracleState => Work.OracleConnectionState;

    public string OracleStatusText => OracleState switch
    {
        OracleConnectionState.Checking => "Проверка…",
        OracleConnectionState.Connected => "Подключено",
        OracleConnectionState.Error => "Ошибка",
        _ => "Не проверено"
    };

    public string OracleStatusToolTip => Work.OracleConnectionStatusDetail;

    public bool IsOracleStatusUnknown => OracleState == OracleConnectionState.Unknown;

    public bool IsOracleStatusChecking => OracleState == OracleConnectionState.Checking;

    public bool IsOracleStatusConnected => OracleState == OracleConnectionState.Connected;

    public bool IsOracleStatusError => OracleState == OracleConnectionState.Error;

    // Совместимость с текущим binding до визуальной интеграции Oracle-состояний.
    public string WmsStatusText => OracleStatusText;

    public string WmsStatusToolTip => OracleStatusToolTip;

    public string NoDataValue => "–";

    public string NoDataText => "Нет данных";

    public async Task RefreshEquipmentStatusAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _equipmentRefreshPending, 1) != 0)
        {
            return;
        }

        try
        {
            var snapshot = await Task.Run(_equipmentSnapshotProvider);

            if (!_disposed)
            {
                ApplyEquipmentSnapshot(snapshot);
            }
        }
        catch
        {
            // Monitoring must retain the last truthful snapshot when Windows printer
            // discovery or settings I/O is temporarily unavailable.
        }
        finally
        {
            Interlocked.Exchange(ref _equipmentRefreshPending, 0);
        }
    }

    public void RefreshMonitoringState()
    {
        if (!_disposed)
        {
            NotifyLineStateChanged();
        }
    }

    internal void RefreshEquipmentStatus()
    {
        ApplyEquipmentSnapshot(_equipmentSnapshotProvider());
    }

    private void ApplyEquipmentSnapshot(AutomaticLineEquipmentSnapshot snapshot)
    {
        if (SetProperty(ref _isScannerRunning, snapshot.IsScannerRunning, nameof(IsScannerRunning)))
        {
            OnPropertyChanged(nameof(ScannerStatusText));
        }

        if (SetProperty(ref _isPrinterInstalled, snapshot.IsPrinterInstalled, nameof(IsPrinterInstalled)))
        {
            OnPropertyChanged(nameof(PrinterStatusText));
        }

        if (SetProperty(ref _isScalesEnabled, snapshot.UseScales, nameof(IsScalesEnabled)))
        {
            OnPropertyChanged(nameof(ScalesStatusText));
        }

        if (!HasEquipmentSnapshot)
        {
            HasEquipmentSnapshot = true;
            OnPropertyChanged(nameof(ScannerStatusText));
            OnPropertyChanged(nameof(PrinterStatusText));
            OnPropertyChanged(nameof(ScalesStatusText));
        }

        NotifyLineStateChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Work.PropertyChanged -= OnWorkPropertyChanged;
        Work.Notifications.CollectionChanged -= OnNotificationsCollectionChanged;
        _disposed = true;
    }

    internal static AutomaticLineState ResolveLineState(
        bool isBusy,
        string? statusMessage,
        AutomaticLineEvent? currentLineEvent,
        bool hasRecentSuccess)
    {
        if (isBusy)
        {
            if (Contains(statusMessage, "печат") || Contains(statusMessage, "печать"))
            {
                return AutomaticLineState.Printing;
            }

            if (Contains(statusMessage, "загруз"))
            {
                return AutomaticLineState.Loading;
            }

            return AutomaticLineState.Processing;
        }

        if (currentLineEvent?.IsError == true)
        {
            return AutomaticLineState.Error;
        }

        if (IsErrorStatus(statusMessage))
        {
            return AutomaticLineState.Error;
        }

        if (currentLineEvent?.IsWarning == true
            || IsWarningStatus(statusMessage))
        {
            return AutomaticLineState.Warning;
        }

        if (hasRecentSuccess)
        {
            return AutomaticLineState.Success;
        }

        return AutomaticLineState.Idle;
    }

    internal static bool AreEnabledPrinterRolesInstalled(
        PrintSettings settings,
        Func<string, bool> isPrinterInstalled)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(isPrinterInstalled);

        var enabledPrinterNames = new List<string>();

        if (settings.PrintEndLabelEnabled)
        {
            enabledPrinterNames.Add(settings.EndLabelPrinterName);
        }

        if (settings.PrintStuffingSheetEnabled)
        {
            enabledPrinterNames.Add(settings.StuffingSheetPrinterName);
        }

        return enabledPrinterNames.Count > 0
            && enabledPrinterNames.All(printerName =>
                !string.IsNullOrWhiteSpace(printerName)
                && isPrinterInstalled(printerName));
    }

    private bool IsSuccessStateVisible => _successStateExpiresAt is { } expiresAt
        && _clock() < expiresAt;

    private UiNotification? LastBoxNotification
    {
        get
        {
            if (!HasLastBox)
            {
                return null;
            }

            return Work.Notifications.FirstOrDefault(notification =>
                ContainsStandaloneNumber(notification.Message, Work.LastProcessedTenam));
        }
    }

    private void OnWorkPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(MainViewModel.CurrentWorkMode))
        {
            ResetAutomaticActivity();
        }

        if ((string.IsNullOrEmpty(eventArgs.PropertyName)
             || eventArgs.PropertyName == nameof(MainViewModel.IsBusy)
             || eventArgs.PropertyName == nameof(MainViewModel.ActiveRequestMode))
            && Work.CurrentWorkMode == WorkMode.Automatic
            && Work.ActiveRequestMode == WorkMode.Automatic
            && Work.IsBusy)
        {
            _hasAutomaticActivity = true;
            _currentLineEvent = null;
            _successStateExpiresAt = null;
        }

        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(MainViewModel.IsBusy)
            || eventArgs.PropertyName == nameof(MainViewModel.ActiveRequestMode)
            || eventArgs.PropertyName == nameof(MainViewModel.StatusMessage)
            || eventArgs.PropertyName == nameof(MainViewModel.CurrentWorkMode))
        {
            NotifyLineStateChanged();
        }

        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(MainViewModel.OracleConnectionState)
            || eventArgs.PropertyName == nameof(MainViewModel.OracleConnectionStatusDetail)
            || eventArgs.PropertyName == nameof(MainViewModel.CurrentOracleQueryTenam))
        {
            NotifyOracleStatusChanged();
            NotifyLineStateChanged();
        }

        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(MainViewModel.LastProcessedTenam))
        {
            NotifyLastBoxChanged();
            NotifyLineStateChanged();
        }
    }

    private void OnNotificationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        var addedNotification = eventArgs.NewItems?
            .OfType<UiNotification>()
            .FirstOrDefault();

        if (addedNotification is not null
            && Work.CurrentWorkMode == WorkMode.Automatic
            && IsAutomaticOperationNotification(addedNotification))
        {
            _hasAutomaticActivity = true;
            _currentLineEvent = ProjectEvent(addedNotification);
            _successStateExpiresAt = _currentLineEvent.IsSuccess
                ? _clock() + SuccessStateDuration
                : null;
        }

        RefreshRecentEvents();
        NotifyLastBoxChanged();
        NotifyLineStateChanged();
    }

    private void RefreshRecentEvents()
    {
        RecentEvents = Work.Notifications
            .Where(notification => !IsManualOperationNotification(notification))
            .Take(RecentEventLimit)
            .Select(ProjectEvent)
            .ToArray();
        OnPropertyChanged(nameof(HasRecentEvents));
    }

    private void ResetAutomaticActivity()
    {
        _hasAutomaticActivity = false;
        _currentLineEvent = null;
        _successStateExpiresAt = null;
    }

    private void NotifyLineStateChanged()
    {
        OnPropertyChanged(nameof(LineState));
        OnPropertyChanged(nameof(LineHeadline));
        OnPropertyChanged(nameof(LineSubtitle));
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsInitializing));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsPrinting));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsWarning));
        OnPropertyChanged(nameof(IsError));
    }

    private void NotifyLastBoxChanged()
    {
        OnPropertyChanged(nameof(LastBoxTenamText));
        OnPropertyChanged(nameof(LastBoxTimeText));
        OnPropertyChanged(nameof(LastBoxResultText));
        OnPropertyChanged(nameof(LastBoxActionText));
        OnPropertyChanged(nameof(HasLastBox));
        OnPropertyChanged(nameof(IsLastBoxSuccess));
        OnPropertyChanged(nameof(IsLastBoxWarning));
        OnPropertyChanged(nameof(IsLastBoxError));
        OnPropertyChanged(nameof(LineSubtitle));
    }

    private void NotifyOracleStatusChanged()
    {
        OnPropertyChanged(nameof(OracleState));
        OnPropertyChanged(nameof(OracleStatusText));
        OnPropertyChanged(nameof(OracleStatusToolTip));
        OnPropertyChanged(nameof(IsOracleStatusUnknown));
        OnPropertyChanged(nameof(IsOracleStatusChecking));
        OnPropertyChanged(nameof(IsOracleStatusConnected));
        OnPropertyChanged(nameof(IsOracleStatusError));
        OnPropertyChanged(nameof(WmsStatusText));
        OnPropertyChanged(nameof(WmsStatusToolTip));
    }

    private string ResolveStatusMessage(string fallback) =>
        string.IsNullOrWhiteSpace(Work.StatusMessage) ? fallback : Work.StatusMessage;

    private string ResolveLineIssueMessage()
    {
        if (IsErrorStatus(Work.StatusMessage) || IsWarningStatus(Work.StatusMessage))
        {
            return ResolveStatusMessage("Нет подробностей");
        }

        return _currentLineEvent?.Message ?? ResolveStatusMessage("Нет подробностей");
    }

    private static bool Contains(string? value, string fragment) =>
        value?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsErrorStatus(string? statusMessage) =>
        Contains(statusMessage, "не удалось")
        || statusMessage?.StartsWith("Ошибка", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsWarningStatus(string? statusMessage) =>
        Contains(statusMessage, "нет веса")
        || Contains(statusMessage, "ожидание ввода веса")
        || Contains(statusMessage, "не настроены принтеры");

    private static string ResolveCompactAction(string message)
    {
        // TODO: replace this isolated best-effort projection with ProcessingEvent.Action
        // once structured processing events are the source of the monitoring screen.
        if (Contains(message, "лист сброса"))
        {
            return Contains(message, "отправлен на печать")
                ? "Лист сброса отправлен на печать"
                : "Лист сброса";
        }

        if (Contains(message, "торцев") || Contains(message, "этикетк"))
        {
            return Contains(message, "отправлен на печать")
                ? "Торцевая этикетка отправлена на печать"
                : "Торцевая этикетка";
        }

        if (Contains(message, "вес"))
        {
            return "Обработка веса";
        }

        return "Обработка данных";
    }

    internal static AutomaticLineEvent ProjectEvent(UiNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new AutomaticLineEvent(
            notification.Timestamp,
            notification.Message,
            ResolveEventSeverity(notification));
    }

    private static AutomaticLineEventSeverity ResolveEventSeverity(UiNotification notification)
    {
        if (notification.IsError)
        {
            return AutomaticLineEventSeverity.Error;
        }

        if (notification.IsWarning
            || Contains(notification.Message, "данные не найдены")
            || Contains(notification.Message, "не найден в БД")
            || IsTransitionRiskMessage(notification.Message))
        {
            return AutomaticLineEventSeverity.Warning;
        }

        if (notification.IsSuccess
            && ((Contains(notification.Message, "короб №")
                 && !Contains(notification.Message, "не найден"))
                || Contains(notification.Message, "отправлен на печать")))
        {
            return AutomaticLineEventSeverity.Success;
        }

        return AutomaticLineEventSeverity.Information;
    }

    private static bool IsTransitionRiskMessage(string message) =>
        Contains(message, "переключение в ручной режим")
        || Contains(message, "не потерять короб")
        || Contains(message, "повторно отскан")
        || Contains(message, "повторное скан")
        || Contains(message, "requiresrescan")
        || Contains(message, "rejectedduringtransition");

    private bool IsAutomaticOperationNotification(UiNotification notification) =>
        !IsManualOperationNotification(notification)
        && (Work.ActiveRequestMode == WorkMode.Automatic
            || Contains(notification.Message, "короб №")
        || Contains(notification.Message, "лист сброса №")
        || Contains(notification.Message, "торцевая этикетка №")
        || (_hasAutomaticActivity
            && !string.IsNullOrWhiteSpace(Work.StatusMessage)
            && Contains(notification.Message, Work.StatusMessage)));

    private static bool IsManualOperationNotification(UiNotification notification) =>
        notification.Message.StartsWith(
            "Ручная обработка:",
            StringComparison.OrdinalIgnoreCase);

    private static bool ContainsStandaloneNumber(string value, string number)
    {
        var searchStart = 0;

        while (searchStart < value.Length)
        {
            var index = value.IndexOf(number, searchStart, StringComparison.Ordinal);

            if (index < 0)
            {
                return false;
            }

            var leftIsDigit = index > 0 && char.IsDigit(value[index - 1]);
            var rightIndex = index + number.Length;
            var rightIsDigit = rightIndex < value.Length && char.IsDigit(value[rightIndex]);

            if (!leftIsDigit && !rightIsDigit)
            {
                return true;
            }

            searchStart = index + number.Length;
        }

        return false;
    }

    private static Func<AutomaticLineEquipmentSnapshot> CreateEquipmentSnapshotProvider(
        IBoxScanner boxScanner)
    {
        return () =>
        {
            var settings = PrintSettingsStore.LoadOrDefault();

            return new AutomaticLineEquipmentSnapshot(
                boxScanner.IsRunning,
                HasInstalledConfiguredPrinter(settings),
                settings.UseScales);
        };
    }

    private static bool HasInstalledConfiguredPrinter(PrintSettings settings)
    {
        try
        {
            if (!settings.PrintEndLabelEnabled && !settings.PrintStuffingSheetEnabled)
            {
                return PrinterDiscovery.GetInstalledPrinters().Count > 0;
            }

            return AreEnabledPrinterRolesInstalled(settings, PrinterDiscovery.IsPrinterInstalled);
        }
        catch
        {
            return false;
        }
    }
}

public enum AutomaticLineState
{
    Idle = 0,
    Loading = 1,
    Processing = 2,
    Printing = 3,
    Success = 4,
    Warning = 5,
    Error = 6,
    Initializing = 7,
    Disabled = 8
}

internal readonly record struct AutomaticLineEquipmentSnapshot(
    bool IsScannerRunning,
    bool IsPrinterInstalled,
    bool UseScales);

public sealed record AutomaticLineEvent(
    DateTime Timestamp,
    string Message,
    AutomaticLineEventSeverity Severity)
{
    public bool IsSuccess => Severity == AutomaticLineEventSeverity.Success;

    public bool IsWarning => Severity == AutomaticLineEventSeverity.Warning;

    public bool IsError => Severity == AutomaticLineEventSeverity.Error;

    public string CategoryIcon => Severity switch
    {
        AutomaticLineEventSeverity.Success => "✓",
        AutomaticLineEventSeverity.Warning => "!",
        AutomaticLineEventSeverity.Error => "×",
        _ => "·"
    };
}

public enum AutomaticLineEventSeverity
{
    Information = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}
