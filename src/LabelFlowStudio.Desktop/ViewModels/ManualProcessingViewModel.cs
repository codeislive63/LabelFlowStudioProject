using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Commands;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;

namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// Presentation-model of the manual processing screen. The complete record set and
/// all business commands remain owned by <see cref="MainViewModel"/>; pagination is
/// only a read-only projection for the table.
/// </summary>
public sealed class ManualProcessingViewModel : ViewModelBase, IDisposable
{
    private static readonly IReadOnlyList<int> AllowedPageSizes = Array.AsReadOnly([10, 25, 50]);
    private static readonly TimeSpan DefaultLoadingShowDelay = TimeSpan.FromMilliseconds(175);
    private static readonly TimeSpan DefaultMinimumLoadingVisible = TimeSpan.FromMilliseconds(250);

    private readonly ObservableCollection<LabelRecord> _pagedRecords = [];
    private readonly ObservableCollection<PageNavigationItem> _pageNavigationItems = [];
    private readonly RelayCommand _firstPageCommand;
    private readonly RelayCommand _previousPageCommand;
    private readonly RelayCommand _nextPageCommand;
    private readonly RelayCommand _lastPageCommand;
    private readonly TimeSpan _loadingShowDelay;
    private readonly TimeSpan _minimumLoadingVisible;
    private readonly SynchronizationContext? _presentationContext;

    private PropertyDescriptor? _sortProperty;
    private ListSortDirection? _sortDirection;
    private CancellationTokenSource? _loadingPresentationCancellation;
    private DateTime _loadingOverlayShownAtUtc;
    private string _loadingTenam = string.Empty;
    private string _tenamInput = string.Empty;
    private int _currentPage = 1;
    private int _pageSize = 10;
    private int _loadingGeneration;
    private bool _isLoadingCycleActive;
    private bool _isLoadingOverlayVisible;
    private bool _loadingHideScheduled;
    private bool _pageRefreshScheduled;
    private bool _disposed;

    public ManualProcessingViewModel(MainViewModel work)
        : this(work, DefaultLoadingShowDelay, DefaultMinimumLoadingVisible)
    {
    }

    internal ManualProcessingViewModel(
        MainViewModel work,
        TimeSpan loadingShowDelay,
        TimeSpan minimumLoadingVisible)
    {
        Work = work ?? throw new ArgumentNullException(nameof(work));
        if (loadingShowDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(loadingShowDelay));
        }

        if (minimumLoadingVisible < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLoadingVisible));
        }

        _loadingShowDelay = loadingShowDelay;
        _minimumLoadingVisible = minimumLoadingVisible;
        _presentationContext = SynchronizationContext.Current;
        _tenamInput = string.Empty;
        PagedRecords = new ReadOnlyObservableCollection<LabelRecord>(_pagedRecords);
        PageNavigationItems = new ReadOnlyObservableCollection<PageNavigationItem>(_pageNavigationItems);

        _firstPageCommand = new RelayCommand(
            () => NavigateToPage(1),
            () => CanNavigateBackward);
        _previousPageCommand = new RelayCommand(
            () => NavigateToPage(CurrentPage - 1),
            () => CanNavigateBackward);
        _nextPageCommand = new RelayCommand(
            () => NavigateToPage(CurrentPage + 1),
            () => CanNavigateForward);
        _lastPageCommand = new RelayCommand(
            () => NavigateToPage(TotalPages),
            () => CanNavigateForward);

        Work.Records.CollectionChanged += OnRecordsCollectionChanged;
        Work.PropertyChanged += OnWorkPropertyChanged;

        RefreshPage();
    }

    /// <summary>
    /// Existing work model. In particular, <see cref="MainViewModel.Records"/> stays
    /// complete and is the source used by printing and other business commands.
    /// </summary>
    public MainViewModel Work { get; }

    /// <summary>
    /// Current page for display only.
    /// </summary>
    public ReadOnlyObservableCollection<LabelRecord> PagedRecords { get; }

    /// <summary>
    /// Semantic alias useful for views that call the display projection "visible records".
    /// </summary>
    public ReadOnlyObservableCollection<LabelRecord> VisibleRecords => PagedRecords;

    public IReadOnlyList<int> PageSizeOptions => AllowedPageSizes;

    /// <summary>
    /// Presentation-value of the manual TENAM field. MainViewModel may clear its
    /// command input after taking a snapshot; this value remains visible for the
    /// operator without leaking a stale automatic box into manual mode.
    /// </summary>
    public string TenamInput
    {
        get => _tenamInput;
        set
        {
            var digitsOnly = new string((value ?? string.Empty)
                .Where(char.IsDigit)
                .ToArray());

            SetProperty(ref _tenamInput, digitsOnly);
        }
    }

    public bool IsLoadingOverlayVisible
    {
        get => _isLoadingOverlayVisible;
        private set
        {
            if (SetProperty(ref _isLoadingOverlayVisible, value))
            {
                OnPropertyChanged(nameof(IsEmptyStateVisible));
            }
        }
    }

    public string LoadingTenam
    {
        get => _loadingTenam;
        private set
        {
            if (SetProperty(ref _loadingTenam, value))
            {
                OnPropertyChanged(nameof(LoadingDescription));
            }
        }
    }

    public string LoadingDescription => string.IsNullOrWhiteSpace(LoadingTenam)
        ? "Получение данных…"
        : $"Получение данных короба {LoadingTenam}…";

    public bool HasRecords => Work.Records.Count > 0;

    public bool CanChangeManualOptions => !Work.IsBusy;

    public bool IsEmptyStateVisible => !HasRecords && !IsLoadingOverlayVisible;

    public bool IsNotFoundState => !HasRecords
        && Work.LastManualProcessingStatus == BoxProcessingStatus.NotFound;

    public bool IsErrorState => !HasRecords
        && Work.LastManualProcessingStatus == BoxProcessingStatus.Error;

    public string EmptyStateTitle => IsNotFoundState
        ? "Короб не найден"
        : IsErrorState
            ? "Не удалось загрузить данные"
            : "Ожидание TENAM";

    public string EmptyStateDescription => IsNotFoundState
        ? "Для указанного TENAM данные отсутствуют"
        : IsErrorState
            ? Work.OracleConnectionState == OracleConnectionState.Error
                ? "Проверьте подключение и повторите попытку"
                : "Проверьте данные короба и повторите попытку"
            : "Введите или отсканируйте номер коробки";

    public string CurrentBoxText => HasRecords
        ? Work.Records[0].Tenam ?? "–"
        : "–";

    public bool IsAutoPrintDisabledStatus =>
        Work.ManualStatusMessage.Contains("Автопечать отключена", StringComparison.OrdinalIgnoreCase);

    public string DataStatusText => IsAutoPrintDisabledStatus
        ? "Автопечать отключена в настройках"
        : Work.LastManualProcessingStatus switch
        {
            BoxProcessingStatus.Success => "Данные загружены",
            BoxProcessingStatus.NeedWeight => "Требуется вес",
            BoxProcessingStatus.Error => "Ошибка данных",
            BoxProcessingStatus.NotFound => "Нет данных",
            _ => "Неизвестно"
        };

    public bool IsDataStatusSuccess => !IsAutoPrintDisabledStatus
        && Work.LastManualProcessingStatus == BoxProcessingStatus.Success;

    public bool IsDataStatusWarning => Work.LastManualProcessingStatus == BoxProcessingStatus.NeedWeight;

    public bool IsDataStatusError => Work.LastManualProcessingStatus == BoxProcessingStatus.Error;

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (!AllowedPageSizes.Contains(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"Supported page sizes: {string.Join(", ", AllowedPageSizes)}.");
            }

            if (_pageSize == value)
            {
                return;
            }

            var firstVisibleItemIndex = (CurrentPage - 1) * _pageSize;
            _pageSize = value;
            OnPropertyChanged();

            var targetPage = TotalPages == 0
                ? 1
                : Math.Clamp((firstVisibleItemIndex / _pageSize) + 1, 1, TotalPages);

            if (_currentPage != targetPage)
            {
                _currentPage = targetPage;
                OnPropertyChanged(nameof(CurrentPage));
            }

            RefreshPage();
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        set => NavigateToPage(value);
    }

    public int TotalItems => Work.Records.Count;

    public int TotalPages => TotalItems == 0
        ? 0
        : (TotalItems + PageSize - 1) / PageSize;

    public int RangeStart => TotalItems == 0
        ? 0
        : ((CurrentPage - 1) * PageSize) + 1;

    public int RangeEnd => TotalItems == 0
        ? 0
        : Math.Min(CurrentPage * PageSize, TotalItems);

    public string RangeText => TotalItems == 0
        ? "Показано 0 из 0"
        : $"Показано {RangeStart}–{RangeEnd} из {TotalItems}";

    public bool HasItems => TotalItems > 0;

    public bool CanNavigateBackward => HasItems && CurrentPage > 1;

    public bool CanNavigateForward => HasItems && CurrentPage < TotalPages;

    public ICommand FirstPageCommand => _firstPageCommand;

    public ICommand PreviousPageCommand => _previousPageCommand;

    public ICommand NextPageCommand => _nextPageCommand;

    public ICommand LastPageCommand => _lastPageCommand;

    /// <summary>
    /// Number buttons and ellipses for the compact pager. First/last navigation
    /// remains available through dedicated commands even when the window is truncated.
    /// </summary>
    public ReadOnlyObservableCollection<PageNavigationItem> PageNavigationItems { get; }

    /// <summary>
    /// The current sort descriptor for a future DataGrid Sorting handler. Sorting is
    /// applied to the complete source before Skip/Take, never to a single page.
    /// </summary>
    public string? SortPropertyName => _sortProperty?.Name;

    public ListSortDirection? SortDirection => _sortDirection;

    public void NavigateToPage(int pageNumber)
    {
        var targetPage = TotalPages == 0
            ? 1
            : Math.Clamp(pageNumber, 1, TotalPages);

        if (_currentPage == targetPage)
        {
            return;
        }

        _currentPage = targetPage;
        OnPropertyChanged(nameof(CurrentPage));
        RefreshPage();
    }

    public void ApplySort(string propertyName, ListSortDirection direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        var property = TypeDescriptor.GetProperties(typeof(LabelRecord)).Find(propertyName, ignoreCase: false)
            ?? throw new ArgumentException(
                $"Property '{propertyName}' does not exist on {nameof(LabelRecord)}.",
                nameof(propertyName));

        _sortProperty = property;
        _sortDirection = direction;
        OnPropertyChanged(nameof(SortPropertyName));
        OnPropertyChanged(nameof(SortDirection));
        RefreshPage(resetToFirstPage: true);
    }

    public void ClearSort()
    {
        if (_sortProperty is null)
        {
            return;
        }

        _sortProperty = null;
        _sortDirection = null;
        OnPropertyChanged(nameof(SortPropertyName));
        OnPropertyChanged(nameof(SortDirection));
        RefreshPage(resetToFirstPage: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Work.Records.CollectionChanged -= OnRecordsCollectionChanged;
        Work.PropertyChanged -= OnWorkPropertyChanged;
        CancelLoadingPresentation();
        _disposed = true;
    }

    private void OnRecordsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.Action == NotifyCollectionChangedAction.Add
            && Work.IsBusy
            && _presentationContext is not null)
        {
            SchedulePageRefresh();
        }
        else
        {
            RefreshPage(resetToFirstPage: eventArgs.Action == NotifyCollectionChangedAction.Reset);
        }

        NotifyManualDataStateChanged();
    }

    private void SchedulePageRefresh()
    {
        if (_pageRefreshScheduled)
        {
            return;
        }

        _pageRefreshScheduled = true;
        _presentationContext!.Post(_ =>
        {
            if (_disposed || !_pageRefreshScheduled)
            {
                return;
            }

            _pageRefreshScheduled = false;
            RefreshPage();
        }, null);
    }

    private void OnWorkPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if ((string.IsNullOrEmpty(eventArgs.PropertyName)
             || eventArgs.PropertyName == nameof(MainViewModel.CurrentOracleQueryTenam)
             || eventArgs.PropertyName == nameof(MainViewModel.ActiveRequestMode))
            && Work.ActiveRequestMode == WorkMode.Manual
            && !string.IsNullOrWhiteSpace(Work.CurrentOracleQueryTenam))
        {
            LoadingTenam = Work.CurrentOracleQueryTenam;
        }

        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(MainViewModel.OracleConnectionState)
            || eventArgs.PropertyName == nameof(MainViewModel.IsBusy)
            || eventArgs.PropertyName == nameof(MainViewModel.ActiveRequestMode)
            || eventArgs.PropertyName == nameof(MainViewModel.ManualStatusMessage))
        {
            UpdateLoadingPresentation();
            OnPropertyChanged(nameof(EmptyStateDescription));
        }

        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(MainViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(CanChangeManualOptions));

            if (!Work.IsBusy && _pageRefreshScheduled)
            {
                _pageRefreshScheduled = false;
                RefreshPage();
            }
        }

        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(MainViewModel.LastManualProcessingStatus))
        {
            NotifyManualDataStateChanged();
        }

        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(MainViewModel.ManualStatusMessage))
        {
            OnPropertyChanged(nameof(IsAutoPrintDisabledStatus));
            OnPropertyChanged(nameof(DataStatusText));
            OnPropertyChanged(nameof(IsDataStatusSuccess));
        }
    }

    private bool IsPrimaryManualDataLoadActive =>
        Work.ActiveRequestMode == WorkMode.Manual
        && Work.IsBusy
        && Work.OracleConnectionState == OracleConnectionState.Checking
        && string.Equals(Work.StatusMessage, "Загрузка", StringComparison.Ordinal);

    private void UpdateLoadingPresentation()
    {
        if (_disposed)
        {
            return;
        }

        if (IsPrimaryManualDataLoadActive)
        {
            BeginLoadingPresentation();
        }
        else
        {
            EndLoadingPresentation();
        }
    }

    private void BeginLoadingPresentation()
    {
        if (_isLoadingCycleActive)
        {
            return;
        }

        _isLoadingCycleActive = true;
        _loadingHideScheduled = false;
        _loadingGeneration++;
        CancelLoadingPresentation();
        _loadingPresentationCancellation = new CancellationTokenSource();

        if (!string.IsNullOrWhiteSpace(Work.CurrentOracleQueryTenam))
        {
            LoadingTenam = Work.CurrentOracleQueryTenam;
        }

        if (!IsLoadingOverlayVisible)
        {
            _ = ShowLoadingPresentationAsync(
                _loadingGeneration,
                _loadingPresentationCancellation.Token);
        }
    }

    private void EndLoadingPresentation()
    {
        if (!_isLoadingCycleActive)
        {
            return;
        }

        _isLoadingCycleActive = false;

        if (!IsLoadingOverlayVisible)
        {
            CancelLoadingPresentation();
            return;
        }

        if (_loadingHideScheduled || _loadingPresentationCancellation is null)
        {
            return;
        }

        _loadingHideScheduled = true;
        _ = HideLoadingPresentationAsync(
            _loadingGeneration,
            _loadingPresentationCancellation.Token);
    }

    private async Task ShowLoadingPresentationAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_loadingShowDelay, cancellationToken).ConfigureAwait(false);
            await RunOnPresentationThreadAsync(() =>
            {
                if (!_disposed
                    && generation == _loadingGeneration
                    && _isLoadingCycleActive
                    && IsPrimaryManualDataLoadActive)
                {
                    _loadingOverlayShownAtUtc = DateTime.UtcNow;
                    IsLoadingOverlayVisible = true;
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Fast requests intentionally never surface the delayed overlay.
        }
    }

    private async Task HideLoadingPresentationAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            var elapsed = DateTime.UtcNow - _loadingOverlayShownAtUtc;
            var remaining = _minimumLoadingVisible - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            }

            await RunOnPresentationThreadAsync(() =>
            {
                if (!_disposed
                    && generation == _loadingGeneration
                    && !_isLoadingCycleActive)
                {
                    IsLoadingOverlayVisible = false;
                }
            }).ConfigureAwait(false);

            if (generation == _loadingGeneration && !_isLoadingCycleActive)
            {
                CancelLoadingPresentation();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer request owns the presentation state.
        }
    }

    private static Task RunOnPresentationThreadAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private void CancelLoadingPresentation()
    {
        var cancellation = _loadingPresentationCancellation;
        _loadingPresentationCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void NotifyManualDataStateChanged()
    {
        OnPropertyChanged(nameof(HasRecords));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(IsNotFoundState));
        OnPropertyChanged(nameof(IsErrorState));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
        OnPropertyChanged(nameof(CurrentBoxText));
        OnPropertyChanged(nameof(DataStatusText));
        OnPropertyChanged(nameof(IsAutoPrintDisabledStatus));
        OnPropertyChanged(nameof(IsDataStatusSuccess));
        OnPropertyChanged(nameof(IsDataStatusWarning));
        OnPropertyChanged(nameof(IsDataStatusError));
    }

    private void RefreshPage(bool resetToFirstPage = false)
    {
        var targetPage = resetToFirstPage || TotalPages == 0
            ? 1
            : Math.Clamp(CurrentPage, 1, TotalPages);

        if (_currentPage != targetPage)
        {
            _currentPage = targetPage;
            OnPropertyChanged(nameof(CurrentPage));
        }

        IEnumerable<LabelRecord> records = Work.Records;
        if (_sortProperty is not null && _sortDirection is not null)
        {
            records = records.OrderBy(
                record => record,
                new LabelRecordPropertyComparer(_sortProperty, _sortDirection.Value));
        }

        var currentPageRecords = records
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToArray();

        _pagedRecords.Clear();
        foreach (var record in currentPageRecords)
        {
            _pagedRecords.Add(record);
        }

        RebuildPageNavigationItems();
        RaisePaginationPropertiesChanged();
    }

    private void RebuildPageNavigationItems()
    {
        _pageNavigationItems.Clear();

        foreach (var pageNumber in BuildPageWindow(CurrentPage, TotalPages))
        {
            if (pageNumber is null)
            {
                _pageNavigationItems.Add(PageNavigationItem.CreateEllipsis());
                continue;
            }

            var capturedPageNumber = pageNumber.Value;
            _pageNavigationItems.Add(
                PageNavigationItem.CreatePage(
                    capturedPageNumber,
                    capturedPageNumber == CurrentPage,
                    () => NavigateToPage(capturedPageNumber)));
        }
    }

    private void RaisePaginationPropertiesChanged()
    {
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(RangeStart));
        OnPropertyChanged(nameof(RangeEnd));
        OnPropertyChanged(nameof(RangeText));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanNavigateBackward));
        OnPropertyChanged(nameof(CanNavigateForward));

        _firstPageCommand.RaiseCanExecuteChanged();
        _previousPageCommand.RaiseCanExecuteChanged();
        _nextPageCommand.RaiseCanExecuteChanged();
        _lastPageCommand.RaiseCanExecuteChanged();
    }

    private static IEnumerable<int?> BuildPageWindow(int currentPage, int totalPages)
    {
        if (totalPages <= 0)
        {
            yield break;
        }

        // Для маленького количества страниц всё помещается без многоточия.
        if (totalPages <= 5)
        {
            for (var page = 1; page <= totalPages; page++)
            {
                yield return page;
            }

            yield break;
        }

        // Начало диапазона:
        // 1 2 3 … 10
        if (currentPage <= 2)
        {
            yield return 1;
            yield return 2;
            yield return 3;
            yield return null;
            yield return totalPages;
            yield break;
        }

        // Третья страница:
        // 1 2 3 4 … 10
        if (currentPage == 3)
        {
            yield return 1;
            yield return 2;
            yield return 3;
            yield return 4;
            yield return null;
            yield return totalPages;
            yield break;
        }

        // Конец диапазона:
        // 1 … 8 9 10
        if (currentPage >= totalPages - 1)
        {
            yield return 1;
            yield return null;
            yield return totalPages - 2;
            yield return totalPages - 1;
            yield return totalPages;
            yield break;
        }

        // Предпоследняя зона:
        // 1 … 7 8 9 10
        if (currentPage == totalPages - 2)
        {
            yield return 1;
            yield return null;
            yield return totalPages - 3;
            yield return totalPages - 2;
            yield return totalPages - 1;
            yield return totalPages;
            yield break;
        }

        // Середина:
        // 1 … 4 5 6 … 10
        yield return 1;
        yield return null;
        yield return currentPage - 1;
        yield return currentPage;
        yield return currentPage + 1;
        yield return null;
        yield return totalPages;
    }

    private sealed class LabelRecordPropertyComparer(
        PropertyDescriptor property,
        ListSortDirection direction) : IComparer<LabelRecord>
    {
        public int Compare(LabelRecord? left, LabelRecord? right)
        {
            var comparison = CompareValues(
                left is null ? null : property.GetValue(left),
                right is null ? null : property.GetValue(right));

            return direction == ListSortDirection.Ascending
                ? comparison
                : -comparison;
        }

        private static int CompareValues(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            if (left is string leftText && right is string rightText)
            {
                return StringComparer.CurrentCultureIgnoreCase.Compare(leftText, rightText);
            }

            return left is IComparable comparable
                ? comparable.CompareTo(right)
                : StringComparer.CurrentCultureIgnoreCase.Compare(left.ToString(), right.ToString());
        }
    }
}

public sealed class PageNavigationItem
{
    private PageNavigationItem(
        int? pageNumber,
        string text,
        bool isCurrent,
        ICommand? command)
    {
        PageNumber = pageNumber;
        Text = text;
        IsCurrent = isCurrent;
        Command = command;
    }

    public int? PageNumber { get; }

    public string Text { get; }

    public bool IsCurrent { get; }

    public bool IsEllipsis => PageNumber is null;

    public ICommand? Command { get; }

    internal static PageNavigationItem CreatePage(
        int pageNumber,
        bool isCurrent,
        Action navigate) =>
        new(
            pageNumber,
            pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            isCurrent,
            new RelayCommand(navigate, () => !isCurrent));

    internal static PageNavigationItem CreateEllipsis() =>
        new(null, "…", false, null);
}
