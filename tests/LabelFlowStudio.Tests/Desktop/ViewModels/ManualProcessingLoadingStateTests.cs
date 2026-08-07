using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class ManualProcessingLoadingStateTests
{
    [Fact]
    public void AutomaticTenam_NeverBecomesAStaleManualSubmission()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWork(new ImmediateProcessingService(
                CreateResponse(BoxProcessingStatus.Success)));
            using var manual = new ManualProcessingViewModel(work);
            manual.TenamInput = "4430500";

            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Automatic);

            Assert.Empty(manual.TenamInput);
            work.Tenam = "4430501";
            Assert.Empty(manual.TenamInput);

            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Manual);

            Assert.Empty(manual.TenamInput);
        });
    }

    [Fact]
    public Task SlowPrimaryLoad_ShowsOneDelayedOverlayAndKeepsItVisibleForTheMinimumInterval() =>
        StaTestRunner.RunAsync(async () =>
        {
            var service = new ControlledProcessingService();
            using var work = CreateWork(service);
            using var manual = new ManualProcessingViewModel(
                work,
                loadingShowDelay: TimeSpan.FromMilliseconds(30),
                minimumLoadingVisible: TimeSpan.FromMilliseconds(100));
            var transitions = new ConcurrentQueue<bool>();
            manual.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(ManualProcessingViewModel.IsLoadingOverlayVisible))
                {
                    transitions.Enqueue(manual.IsLoadingOverlayVisible);
                }
            };

            work.Tenam = "4430558";
            work.LoadRecordsCommand.Execute(null);

            await service.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(manual.IsLoadingOverlayVisible);

            await WaitHelpers.WaitUntilAsync(
                () => manual.IsLoadingOverlayVisible,
                TimeSpan.FromSeconds(2));
            var visibleStopwatch = Stopwatch.StartNew();

            service.Complete(CreateResponse(BoxProcessingStatus.Success));
            await WaitHelpers.WaitUntilAsync(
                () => work.OracleConnectionState == OracleConnectionState.Connected && !work.IsBusy,
                TimeSpan.FromSeconds(2));

            Assert.True(manual.IsLoadingOverlayVisible);

            await WaitHelpers.WaitUntilAsync(
                () => !manual.IsLoadingOverlayVisible,
                TimeSpan.FromSeconds(2));

            Assert.True(
                visibleStopwatch.Elapsed >= TimeSpan.FromMilliseconds(80),
                $"Overlay was visible only for {visibleStopwatch.Elapsed}.");
            Assert.Equal([true, false], transitions.ToArray());
            Assert.Empty(work.Tenam);
            Assert.Equal("4430558", manual.TenamInput);
            Assert.Equal(BoxProcessingStatus.Success, work.LastProcessingStatus);
        });

    [Fact]
    public Task FastPrimaryLoad_DoesNotFlashTheDelayedOverlay() =>
        StaTestRunner.RunAsync(async () =>
        {
            using var work = CreateWork(new ImmediateProcessingService(
                CreateResponse(BoxProcessingStatus.NotFound)));
            using var manual = new ManualProcessingViewModel(
                work,
                loadingShowDelay: TimeSpan.FromMilliseconds(80),
                minimumLoadingVisible: TimeSpan.FromMilliseconds(30));
            var transitionCount = 0;
            manual.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(ManualProcessingViewModel.IsLoadingOverlayVisible))
                {
                    Interlocked.Increment(ref transitionCount);
                }
            };

            work.Tenam = "4430559";
            work.LoadRecordsCommand.Execute(null);

            await WaitHelpers.WaitUntilAsync(
                () => work.OracleConnectionState == OracleConnectionState.Connected && !work.IsBusy,
                TimeSpan.FromSeconds(2));
            await Task.Delay(120);

            Assert.False(manual.IsLoadingOverlayVisible);
            Assert.Equal(0, Volatile.Read(ref transitionCount));
            Assert.True(manual.IsNotFoundState);
            Assert.Equal("Короб не найден", manual.EmptyStateTitle);
            Assert.Empty(work.Tenam);
            Assert.Equal("4430559", manual.TenamInput);
            Assert.Equal(BoxProcessingStatus.NotFound, work.LastProcessingStatus);
        });

    [Fact]
    public Task FailedPrimaryLoad_ClosesOverlayAndExposesSafeInlineErrorState() =>
        StaTestRunner.RunAsync(async () =>
        {
            var service = new ControlledProcessingService();
            using var work = CreateWork(service);
            using var manual = new ManualProcessingViewModel(
                work,
                loadingShowDelay: TimeSpan.Zero,
                minimumLoadingVisible: TimeSpan.FromMilliseconds(30));

            work.Tenam = "4430560";
            work.LoadRecordsCommand.Execute(null);

            await service.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(
                () => manual.IsLoadingOverlayVisible,
                TimeSpan.FromSeconds(2));

            service.Fail(new InvalidOperationException("Data Source=secret;Password=top-secret"));

            await WaitHelpers.WaitUntilAsync(
                () => work.OracleConnectionState == OracleConnectionState.Error
                      && !work.IsBusy
                      && !manual.IsLoadingOverlayVisible,
                TimeSpan.FromSeconds(2));

            Assert.True(manual.IsErrorState);
            Assert.Equal("Не удалось загрузить данные", manual.EmptyStateTitle);
            Assert.Equal("Проверьте подключение и повторите попытку", manual.EmptyStateDescription);
            Assert.DoesNotContain("secret", manual.EmptyStateDescription, StringComparison.OrdinalIgnoreCase);
            Assert.All(
                work.Notifications,
                notification => Assert.DoesNotContain(
                    "secret",
                    notification.Message,
                    StringComparison.OrdinalIgnoreCase));
            Assert.Empty(work.Tenam);
            Assert.Equal("4430560", manual.TenamInput);
            Assert.Equal(BoxProcessingStatus.Error, work.LastProcessingStatus);
        });

    private static MainViewModel CreateWork(IBoxProcessingService service) =>
        new(
            service,
            new NoOpWeightService(),
            new FakeScanner(),
            NullLogger<MainViewModel>.Instance);

    private static void RaiseWorkModeChangedWithoutPersistence(MainViewModel work, WorkMode mode)
    {
        var field = typeof(MainViewModel).GetField(
            "_currentWorkMode",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var method = typeof(ViewModelBase).GetMethod(
            "OnPropertyChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.NotNull(method);
        field.SetValue(work, mode);
        method.Invoke(work, [nameof(MainViewModel.CurrentWorkMode)]);
    }

    private static BoxProcessingResponse CreateResponse(BoxProcessingStatus status) =>
        new(
            status,
            status == BoxProcessingStatus.NotFound ? "Данные не найдены" : "Данные загружены",
            Array.Empty<LabelRecord>(),
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

    private sealed class ImmediateProcessingService(BoxProcessingResponse response) : IBoxProcessingService
    {
        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken) => Task.FromResult(response);
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
