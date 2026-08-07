using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class WorkPresentationViewModelTests
{
    [Fact]
    public void WorkSection_SelectsStablePresentationModelForCurrentWorkMode()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel(new ImmediateProcessingService(), new FakeScanner());
            using var automatic = CreateAutomaticViewModel(work);
            var manual = new ManualProcessingViewModel(work);

            using var section = new WorkSectionViewModel(work, automatic, manual);
            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Automatic);

            Assert.Same(automatic, section.CurrentModeViewModel);
            Assert.True(section.IsAutomaticMode);
            Assert.False(section.IsManualMode);

            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Manual);

            Assert.Same(manual, section.CurrentModeViewModel);
            Assert.False(section.IsAutomaticMode);
            Assert.True(section.IsManualMode);

            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Automatic);

            Assert.Same(automatic, section.CurrentModeViewModel);
        });
    }

    [Fact]
    public void WorkSection_DisposeStopsListeningToModeChanges()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel(new ImmediateProcessingService(), new FakeScanner());
            using var automatic = CreateAutomaticViewModel(work);
            var manual = new ManualProcessingViewModel(work);

            var section = new WorkSectionViewModel(work, automatic, manual);
            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Manual);
            section.Dispose();

            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Automatic);

            Assert.Same(manual, section.CurrentModeViewModel);
        });
    }

    [Fact]
    public Task AutomaticLine_ProjectsLastProcessedBoxAndCurrentRunEvents() =>
        StaTestRunner.RunAsync(async () =>
        {
            var service = new ImmediateProcessingService
            {
                Response = new BoxProcessingResponse(
                    BoxProcessingStatus.Success,
                    "Обработка завершена",
                    [],
                    null,
                    PrintPlan.None)
            };

            using var work = CreateWorkViewModel(service, new FakeScanner());
            SetWorkModeWithoutPersistence(work, WorkMode.Automatic);
            using var automatic = CreateAutomaticViewModel(work);
            work.Tenam = "4340558";

            work.LoadRecordsCommand.Execute(null);

            await service.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(() => !work.IsBusy, TimeSpan.FromSeconds(2));

            Assert.Equal(AutomaticLineState.Success, automatic.LineState);
            Assert.Equal("4340558", automatic.LastBoxTenamText);
            Assert.NotEqual("–", automatic.LastBoxTimeText);
            Assert.Equal("Успешно", automatic.LastBoxResultText);
            Assert.Equal("Обработка данных", automatic.LastBoxActionText);
            Assert.True(automatic.IsLastBoxSuccess);
            Assert.False(automatic.IsLastBoxWarning);
            Assert.False(automatic.IsLastBoxError);
            Assert.NotEmpty(automatic.RecentEvents);
            Assert.True(automatic.RecentEvents.Count <= 5);
        });

    [Fact]
    public void AutomaticLine_UsesExplicitNoDataValuesWithoutInventingKpisOrLastBox()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel(new ImmediateProcessingService(), new FakeScanner());
            using var automatic = CreateAutomaticViewModel(work);

            Assert.Equal("–", automatic.NoDataValue);
            Assert.Equal("Нет данных", automatic.NoDataText);
            Assert.Equal("–", automatic.LastBoxTenamText);
            Assert.Equal("–", automatic.LastBoxTimeText);
            Assert.Equal("Нет данных", automatic.LastBoxResultText);
            Assert.Equal("Нет данных", automatic.LastBoxActionText);
            Assert.False(automatic.IsLastBoxSuccess);
            Assert.Empty(automatic.RecentEvents);
        });
    }

    [Fact]
    public Task AutomaticLine_UsesLatestMatchingEventForLastBoxSeverity() =>
        StaTestRunner.RunAsync(async () =>
        {
            var service = new ImmediateProcessingService();
            using var work = CreateWorkViewModel(service, new FakeScanner());
            SetWorkModeWithoutPersistence(work, WorkMode.Automatic);
            using var automatic = CreateAutomaticViewModel(work);
            work.Tenam = "4340558";
            work.LoadRecordsCommand.Execute(null);

            await service.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(() => !work.IsBusy, TimeSpan.FromSeconds(2));

            AddNotification(work, "Короб №4340558: принтер не найден", NotificationCategory.Error);

            Assert.Equal("Ошибка", automatic.LastBoxResultText);
            Assert.True(automatic.IsLastBoxError);
            Assert.False(automatic.IsLastBoxSuccess);
            Assert.Equal(AutomaticLineState.Error, automatic.LineState);
        });

    [Fact]
    public void AutomaticLine_RecentEventsAreLimitedToFiveNewestNotifications()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel(new ImmediateProcessingService(), new FakeScanner());
            using var automatic = CreateAutomaticViewModel(work);

            for (var index = 1; index <= 7; index++)
            {
                AddNotification(work, $"Событие {index}", NotificationCategory.Success);
            }

            Assert.Equal(5, automatic.RecentEvents.Count);
            Assert.Equal("Событие 7", automatic.RecentEvents[0].Message);
            Assert.Equal("Событие 3", automatic.RecentEvents[^1].Message);
        });
    }

    [Fact]
    public void AutomaticLine_RefreshesRealEquipmentProjectionFromSnapshot()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel(new ImmediateProcessingService(), new FakeScanner());
            SetWorkModeWithoutPersistence(work, WorkMode.Automatic);
            var snapshot = new AutomaticLineEquipmentSnapshot(
                IsScannerRunning: false,
                IsPrinterInstalled: false,
                UseScales: false);

            using var automatic = new AutomaticLineViewModel(work, () => snapshot);
            Assert.Equal(AutomaticLineState.Initializing, automatic.LineState);
            Assert.True(automatic.IsInitializing);
            Assert.False(automatic.IsSuccess);

            automatic.RefreshEquipmentStatus();

            Assert.True(automatic.HasEquipmentSnapshot);
            Assert.False(automatic.IsScannerRunning);
            Assert.Equal("Остановлен", automatic.ScannerStatusText);
            Assert.Equal("Не найден", automatic.PrinterStatusText);
            Assert.Equal("Отключены", automatic.ScalesStatusText);
            Assert.Equal("Не проверено", automatic.WmsStatusText);
            Assert.Equal(AutomaticLineState.Error, automatic.LineState);
            Assert.False(automatic.IsSuccess);

            snapshot = new AutomaticLineEquipmentSnapshot(
                IsScannerRunning: true,
                IsPrinterInstalled: true,
                UseScales: true);
            automatic.RefreshEquipmentStatus();

            Assert.True(automatic.IsScannerRunning);
            Assert.Equal("Работает", automatic.ScannerStatusText);
            Assert.True(automatic.IsPrinterInstalled);
            Assert.Equal("Установлен", automatic.PrinterStatusText);
            Assert.True(automatic.IsScalesEnabled);
            Assert.Equal("Включены в настройках", automatic.ScalesStatusText);
            Assert.Equal(AutomaticLineState.Idle, automatic.LineState);
        });
    }

    [Fact]
    public void AutomaticLine_ResolvesAllVisualProcessingStatesWithoutChangingWorkLogic()
    {
        var warning = AutomaticLineViewModel.ProjectEvent(
            new UiNotification(DateTime.Now, "Требуется вес", NotificationCategory.Warning));
        var error = AutomaticLineViewModel.ProjectEvent(
            new UiNotification(DateTime.Now, "Ошибка сканера", NotificationCategory.Error));

        Assert.Equal(
            AutomaticLineState.Idle,
            AutomaticLineViewModel.ResolveLineState(false, null, null, false));
        Assert.Equal(
            AutomaticLineState.Loading,
            AutomaticLineViewModel.ResolveLineState(true, "Загрузка", null, false));
        Assert.Equal(
            AutomaticLineState.Processing,
            AutomaticLineViewModel.ResolveLineState(true, "Проверка веса", null, false));
        Assert.Equal(
            AutomaticLineState.Printing,
            AutomaticLineViewModel.ResolveLineState(true, "Печать листа сброса", null, true));
        Assert.Equal(
            AutomaticLineState.Success,
            AutomaticLineViewModel.ResolveLineState(false, "Готово", null, true));
        Assert.Equal(
            AutomaticLineState.Warning,
            AutomaticLineViewModel.ResolveLineState(false, "Готово", warning, false));
        Assert.Equal(
            AutomaticLineState.Error,
            AutomaticLineViewModel.ResolveLineState(false, "Готово", error, false));
        Assert.Equal(
            AutomaticLineState.Warning,
            AutomaticLineViewModel.ResolveLineState(
                false,
                "Нет веса в БД. Ожидание ввода веса",
                null,
                false));

        var success = AutomaticLineViewModel.ProjectEvent(
            new UiNotification(
                DateTime.Now,
                "Короб №4340558: обработка завершена",
                NotificationCategory.Success));
        Assert.Equal(
            AutomaticLineState.Warning,
            AutomaticLineViewModel.ResolveLineState(
                false,
                "Не настроены принтеры для быстрой печати",
                success,
                true));
    }

    [Fact]
    public void AutomaticLine_NotFoundEventIsWarningAndNeverConfirmedSuccess()
    {
        var projected = AutomaticLineViewModel.ProjectEvent(
            new UiNotification(
                DateTime.Now,
                "Короб №4340558: данные не найдены",
                NotificationCategory.Success));

        Assert.True(projected.IsWarning);
        Assert.False(projected.IsSuccess);
        Assert.False(projected.IsError);
    }

    [Fact]
    public void AutomaticLine_EnteringAutomaticModeDoesNotReuseManualErrorAsLineHealth()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel(new ImmediateProcessingService(), new FakeScanner());
            SetWorkModeWithoutPersistence(work, WorkMode.Manual);
            using var automatic = CreateAutomaticViewModel(work);

            AddNotification(work, "Ошибка ручной обработки", NotificationCategory.Error);
            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Automatic);

            Assert.Equal(AutomaticLineState.Initializing, automatic.LineState);
            Assert.Equal("Инициализация линии", automatic.LineHeadline);
            Assert.Equal("Проверка состояния сканера", automatic.LineSubtitle);
        });
    }

    [Fact]
    public Task AutomaticLine_RecentSuccessReturnsToMonitoringIdleState() =>
        StaTestRunner.RunAsync(async () =>
        {
            var now = new DateTime(2026, 8, 6, 9, 0, 0);
            var service = new ImmediateProcessingService();
            using var work = CreateWorkViewModel(service, new FakeScanner());
            SetWorkModeWithoutPersistence(work, WorkMode.Automatic);
            using var automatic = new AutomaticLineViewModel(
                work,
                () => new AutomaticLineEquipmentSnapshot(true, false, false),
                () => now);
            automatic.RefreshEquipmentStatus();
            work.Tenam = "4340558";

            work.LoadRecordsCommand.Execute(null);

            await service.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(() => !work.IsBusy, TimeSpan.FromSeconds(2));
            Assert.Equal(AutomaticLineState.Success, automatic.LineState);

            now = now.AddSeconds(7);
            automatic.RefreshMonitoringState();

            Assert.Equal(AutomaticLineState.Idle, automatic.LineState);
            Assert.Equal("Линия работает", automatic.LineHeadline);
            Assert.Equal("Ожидание следующего короба", automatic.LineSubtitle);
        });

    [Fact]
    public void AutomaticLine_PrinterIsInstalledOnlyWhenEveryEnabledRoleIsAvailable()
    {
        var installedPrinters = new HashSet<string>(StringComparer.Ordinal)
        {
            "End printer"
        };
        var settings = new PrintSettings
        {
            PrintEndLabelEnabled = true,
            EndLabelPrinterName = "End printer",
            PrintStuffingSheetEnabled = true,
            StuffingSheetPrinterName = "Drop printer"
        };

        bool IsInstalled(string printerName) => installedPrinters.Contains(printerName);

        Assert.False(AutomaticLineViewModel.AreEnabledPrinterRolesInstalled(settings, IsInstalled));

        installedPrinters.Add("Drop printer");

        Assert.True(AutomaticLineViewModel.AreEnabledPrinterRolesInstalled(settings, IsInstalled));

        settings.StuffingSheetPrinterName = string.Empty;

        Assert.False(AutomaticLineViewModel.AreEnabledPrinterRolesInstalled(settings, IsInstalled));

        settings.PrintStuffingSheetEnabled = false;

        Assert.True(AutomaticLineViewModel.AreEnabledPrinterRolesInstalled(settings, IsInstalled));

        settings.PrintEndLabelEnabled = false;

        Assert.False(AutomaticLineViewModel.AreEnabledPrinterRolesInstalled(settings, IsInstalled));
    }

    private static AutomaticLineViewModel CreateAutomaticViewModel(MainViewModel work) =>
        new(
            work,
            () => new AutomaticLineEquipmentSnapshot(
                IsScannerRunning: true,
                IsPrinterInstalled: false,
                UseScales: false));

    private static MainViewModel CreateWorkViewModel(
        IBoxProcessingService processingService,
        IBoxScanner scanner)
    {
        return new MainViewModel(
            processingService,
            new NoOpWeightService(),
            scanner,
            NullLogger<MainViewModel>.Instance);
    }

    private static void AddNotification(
        MainViewModel work,
        string message,
        NotificationCategory category)
    {
        var method = typeof(MainViewModel).GetMethod(
            "AddNotification",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(work, [message, category]);
    }

    private static void SetWorkModeWithoutPersistence(MainViewModel work, WorkMode mode)
    {
        var field = typeof(MainViewModel).GetField(
            "_currentWorkMode",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field.SetValue(work, mode);
    }

    private static void RaiseWorkModeChangedWithoutPersistence(MainViewModel work, WorkMode mode)
    {
        SetWorkModeWithoutPersistence(work, mode);

        var method = typeof(ViewModelBase).GetMethod(
            "OnPropertyChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(work, [nameof(MainViewModel.CurrentWorkMode)]);
    }

    private sealed class ImmediateProcessingService : IBoxProcessingService
    {
        public BoxProcessingResponse Response { get; set; } = new(
            BoxProcessingStatus.Success,
            "Обработка завершена",
            [],
            null,
            PrintPlan.None);

        public TaskCompletionSource Called { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            Called.TrySetResult();
            return Task.FromResult(Response);
        }
    }

    private sealed class NoOpWeightService : IBoxWeightService
    {
        public Task<BoxWeightUpdateResult> UpdateWeightAsync(
            string tenam,
            decimal weight,
            CancellationToken cancellationToken) =>
            Task.FromResult(BoxWeightUpdateResult.Success());
    }

    private sealed class FakeScanner : IBoxScanner
    {
        public event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived
        {
            add { }
            remove { }
        }

        public bool IsRunning { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
