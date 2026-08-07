using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class OracleConnectionStateTests
{
    [Fact]
    public Task Request_TransitionsFromUnknownThroughCheckingToConnected() =>
        StaTestRunner.RunAsync(async () =>
        {
            var service = new ControlledProcessingService();
            using var work = CreateWork(service);
            SetWorkModeWithoutPersistence(work, WorkMode.Automatic);
            using var automatic = CreateAutomatic(work);
            automatic.RefreshEquipmentStatus();
            var projectedChanges = new List<string>();
            automatic.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is not null)
                {
                    projectedChanges.Add(args.PropertyName);
                }
            };

            Assert.Equal(OracleConnectionState.Unknown, work.OracleConnectionState);
            Assert.True(automatic.IsOracleStatusUnknown);
            Assert.Equal("Не проверено", automatic.OracleStatusText);

            work.Tenam = "4430558";
            work.LoadRecordsCommand.Execute(null);

            await service.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(OracleConnectionState.Checking, work.OracleConnectionState);
            Assert.Equal("4430558", work.CurrentOracleQueryTenam);
            Assert.True(automatic.IsOracleStatusChecking);
            Assert.Equal("Проверка…", automatic.OracleStatusText);
            Assert.Equal(AutomaticLineState.Loading, automatic.LineState);
            Assert.Equal("Получение данных", automatic.LineHeadline);
            Assert.Equal("Получение данных коробки 4430558…", automatic.LineSubtitle);

            service.Complete(CreateResponse(BoxProcessingStatus.Success));
            await WaitForTerminalStateAsync(work, OracleConnectionState.Connected);

            Assert.Empty(work.CurrentOracleQueryTenam);
            Assert.True(automatic.IsOracleStatusConnected);
            Assert.Equal("Подключено", automatic.OracleStatusText);
            Assert.Contains("успешно", automatic.OracleStatusToolTip, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(AutomaticLineViewModel.OracleStatusText), projectedChanges);
            Assert.Contains(nameof(AutomaticLineViewModel.WmsStatusText), projectedChanges);
        });

    [Fact]
    public Task RequestException_TransitionsFromUnknownThroughCheckingToErrorWithoutLeakingDetails() =>
        StaTestRunner.RunAsync(async () =>
        {
            var service = new ControlledProcessingService();
            using var work = CreateWork(service);
            SetWorkModeWithoutPersistence(work, WorkMode.Automatic);
            using var automatic = CreateAutomatic(work);
            automatic.RefreshEquipmentStatus();

            work.Tenam = "4430558";
            work.LoadRecordsCommand.Execute(null);

            await service.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(OracleConnectionState.Checking, work.OracleConnectionState);

            service.Fail(new InvalidOperationException("Data Source=secret;Password=top-secret"));
            await WaitForTerminalStateAsync(work, OracleConnectionState.Error);

            Assert.Empty(work.CurrentOracleQueryTenam);
            Assert.True(automatic.IsOracleStatusError);
            Assert.Equal("Ошибка", automatic.OracleStatusText);
            Assert.Equal("Не удалось получить данные из базы данных.", automatic.OracleStatusToolTip);
            Assert.DoesNotContain("secret", automatic.OracleStatusToolTip, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(AutomaticLineState.Error, automatic.LineState);
            Assert.Equal("Не удалось получить данные из базы данных.", automatic.LineSubtitle);
        });

    [Fact]
    public Task RuntimeState_TransitionsFromConnectedToErrorAndBackToConnectedForNotFound() =>
        StaTestRunner.RunAsync(async () =>
        {
            var service = new SequencedProcessingService(
                () => Task.FromResult(CreateResponse(BoxProcessingStatus.Success)),
                () => Task.FromException<BoxProcessingResponse>(new InvalidOperationException("Oracle unavailable")),
                () => Task.FromResult(CreateResponse(BoxProcessingStatus.NotFound)));
            using var work = CreateWork(service);
            using var automatic = CreateAutomatic(work);

            await ExecuteRequestAsync(work, service, expectedCallCount: 1);
            Assert.Equal(OracleConnectionState.Connected, work.OracleConnectionState);

            await ExecuteRequestAsync(work, service, expectedCallCount: 2);
            Assert.Equal(OracleConnectionState.Error, work.OracleConnectionState);

            await ExecuteRequestAsync(work, service, expectedCallCount: 3);
            Assert.Equal(OracleConnectionState.Connected, work.OracleConnectionState);
            Assert.True(automatic.IsOracleStatusConnected);
            Assert.Equal("Подключено", automatic.WmsStatusText);
        });

    [Fact]
    public Task BusinessErrorResponse_StillConfirmsConnectedOracle() =>
        StaTestRunner.RunAsync(async () =>
        {
            var service = new SequencedProcessingService(
                () => Task.FromResult(CreateResponse(BoxProcessingStatus.Error)));
            using var work = CreateWork(service);

            await ExecuteRequestAsync(work, service, expectedCallCount: 1);

            Assert.Equal(OracleConnectionState.Connected, work.OracleConnectionState);
            Assert.Equal("Последний запрос к базе данных выполнен успешно.", work.OracleConnectionStatusDetail);
        });

    private static MainViewModel CreateWork(IBoxProcessingService service) =>
        new(
            service,
            new NoOpWeightService(),
            new FakeScanner(),
            NullLogger<MainViewModel>.Instance);

    private static AutomaticLineViewModel CreateAutomatic(MainViewModel work) =>
        new(
            work,
            () => new AutomaticLineEquipmentSnapshot(
                IsScannerRunning: true,
                IsPrinterInstalled: false,
                UseScales: false));

    private static void SetWorkModeWithoutPersistence(MainViewModel work, WorkMode mode)
    {
        var field = typeof(MainViewModel).GetField(
            "_currentWorkMode",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(field);
        field.SetValue(work, mode);
    }

    private static async Task ExecuteRequestAsync(
        MainViewModel work,
        SequencedProcessingService service,
        int expectedCallCount)
    {
        work.Tenam = $"44305{expectedCallCount:00}";
        work.LoadRecordsCommand.Execute(null);

        await WaitHelpers.WaitUntilAsync(
            () => service.CallCount >= expectedCallCount && !work.IsBusy,
            TimeSpan.FromSeconds(2));
    }

    private static Task WaitForTerminalStateAsync(
        MainViewModel work,
        OracleConnectionState expectedState) =>
        WaitHelpers.WaitUntilAsync(
            () => work.OracleConnectionState == expectedState && !work.IsBusy,
            TimeSpan.FromSeconds(2));

    private static BoxProcessingResponse CreateResponse(BoxProcessingStatus status) =>
        new(
            status,
            status switch
            {
                BoxProcessingStatus.NotFound => "Данные не найдены",
                BoxProcessingStatus.Error => "Обнаружен конфликт веса",
                _ => "Данные загружены"
            },
            [],
            null,
            PrintPlan.None);

    private sealed class ControlledProcessingService : IBoxProcessingService
    {
        private readonly TaskCompletionSource<BoxProcessingResponse> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Called { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            Called.TrySetResult();
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(BoxProcessingResponse response) =>
            _completion.TrySetResult(response);

        public void Fail(Exception exception) =>
            _completion.TrySetException(exception);
    }

    private sealed class SequencedProcessingService : IBoxProcessingService
    {
        private readonly Queue<Func<Task<BoxProcessingResponse>>> _steps;
        private int _callCount;

        public SequencedProcessingService(params Func<Task<BoxProcessingResponse>>[] steps)
        {
            _steps = new Queue<Func<Task<BoxProcessingResponse>>>(steps);
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return _steps.Dequeue().Invoke();
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

        public bool IsRunning => true;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
