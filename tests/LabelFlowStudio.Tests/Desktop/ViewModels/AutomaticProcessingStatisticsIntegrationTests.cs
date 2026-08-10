using System.Collections.Concurrent;
using System.Reflection;
using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Statistics;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Core.Statistics;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class AutomaticProcessingStatisticsIntegrationTests
{
    [Theory]
    [InlineData(BoxProcessingStatus.Success, AutomaticProcessingOutcome.Success)]
    [InlineData(BoxProcessingStatus.NotFound, AutomaticProcessingOutcome.Warning)]
    [InlineData(BoxProcessingStatus.NeedWeight, AutomaticProcessingOutcome.Warning)]
    [InlineData(BoxProcessingStatus.Error, AutomaticProcessingOutcome.Error)]
    public Task AutomaticRequest_RecordsExactlyOneTypedFinalOutcome(
        BoxProcessingStatus responseStatus,
        AutomaticProcessingOutcome expectedOutcome) =>
        StaTestRunner.RunAsync(async () =>
        {
            var statistics = new RecordingStatisticsService();
            var processing = new ImmediateProcessingService(CreateResponse(responseStatus));
            using var viewModel = CreateViewModel(processing, new RunningScanner(), statistics);
            SetWorkModeWithoutPersistence(viewModel, WorkMode.Automatic);

            viewModel.ReceiveTenamFromScanner("4430558");

            await processing.Called.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(
                () => statistics.CompletionCount == 1,
                TimeSpan.FromSeconds(2));

            var completion = Assert.Single(statistics.Completions);
            Assert.Equal("4430558", completion.Attempt.Tenam);
            Assert.Equal(expectedOutcome, completion.Outcome);
            Assert.Equal(1, statistics.BeginCount);
        });

    [Fact]
    public Task ManualRequest_DoesNotAffectStatistics_WhenAutomaticLineRemainsEnabled() =>
        StaTestRunner.RunAsync(async () =>
        {
            var statistics = new RecordingStatisticsService();
            var processing = new ImmediateProcessingService(
                CreateResponse(BoxProcessingStatus.Success));
            using var viewModel = CreateViewModel(processing, new RunningScanner(), statistics);
            SetWorkModeWithoutPersistence(viewModel, WorkMode.Automatic);

            Assert.True(viewModel.ReceiveManualTenam("4430558"));

            await processing.Called.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(() => !viewModel.IsBusy, TimeSpan.FromSeconds(2));

            Assert.Equal(WorkMode.Automatic, viewModel.CurrentWorkMode);
            Assert.Equal(0, statistics.BeginCount);
            Assert.Empty(statistics.Completions);
        });

    [Fact]
    public Task ProcessingException_AfterAutomaticStart_RecordsOneError() =>
        StaTestRunner.RunAsync(async () =>
        {
            var statistics = new RecordingStatisticsService();
            using var viewModel = CreateViewModel(
                new ThrowingProcessingService(),
                new RunningScanner(),
                statistics);
            SetWorkModeWithoutPersistence(viewModel, WorkMode.Automatic);

            viewModel.ReceiveTenamFromScanner("4430558");

            await WaitHelpers.WaitUntilAsync(
                () => statistics.CompletionCount == 1,
                TimeSpan.FromSeconds(2));

            var completion = Assert.Single(statistics.Completions);
            Assert.Equal(AutomaticProcessingOutcome.Error, completion.Outcome);
            Assert.Equal(1, statistics.BeginCount);
        });

    [Fact]
    public Task CancellationAfterAutomaticStart_RecordsOneWarning() =>
        StaTestRunner.RunAsync(async () =>
        {
            var statistics = new RecordingStatisticsService();
            var processing = new CancellableProcessingService();
            using var viewModel = CreateViewModel(processing, new RunningScanner(), statistics);
            SetWorkModeWithoutPersistence(viewModel, WorkMode.Automatic);

            viewModel.ReceiveTenamFromScanner("4430558");
            await processing.Started.WaitAsync(TimeSpan.FromSeconds(2));

            var startNewCancellation = typeof(MainViewModel).GetMethod(
                "StartNewLoadCancellation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(startNewCancellation);
            _ = startNewCancellation.Invoke(viewModel, null);

            await WaitHelpers.WaitUntilAsync(
                () => statistics.CompletionCount == 1,
                TimeSpan.FromSeconds(2));

            Assert.Equal(
                AutomaticProcessingOutcome.Warning,
                Assert.Single(statistics.Completions).Outcome);
            Assert.Equal(1, statistics.BeginCount);
        });

    [Fact]
    public Task LatestPendingScannerRequest_RecordsOnlyRequestsThatActuallyStart() =>
        StaTestRunner.RunAsync(async () =>
        {
            var statistics = new RecordingStatisticsService();
            var processing = new BlockingFirstProcessingService();
            using var viewModel = CreateViewModel(processing, new RunningScanner(), statistics);
            SetWorkModeWithoutPersistence(viewModel, WorkMode.Automatic);

            viewModel.ReceiveTenamFromScanner("4430551");
            await processing.FirstStarted.WaitAsync(TimeSpan.FromSeconds(2));

            viewModel.ReceiveTenamFromScanner("4430552");
            viewModel.ReceiveTenamFromScanner("4430553");
            processing.ReleaseFirst();

            await processing.SecondStarted.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(
                () => statistics.CompletionCount == 2,
                TimeSpan.FromSeconds(2));

            Assert.Equal(
                ["4430551", "4430553"],
                statistics.Completions.Select(item => item.Attempt.Tenam).ToArray());
            Assert.Equal(2, statistics.BeginCount);
        });

    [Fact]
    public Task WorkModeChangeDuringRequest_DoesNotReclassifyAutomaticAttempt() =>
        StaTestRunner.RunAsync(async () =>
        {
            var statistics = new RecordingStatisticsService();
            var processing = new BlockingFirstProcessingService();
            using var viewModel = CreateViewModel(processing, new RunningScanner(), statistics);
            SetWorkModeWithoutPersistence(viewModel, WorkMode.Automatic);

            viewModel.ReceiveTenamFromScanner("4430558");
            await processing.FirstStarted.WaitAsync(TimeSpan.FromSeconds(2));

            SetWorkModeWithoutPersistence(viewModel, WorkMode.Manual);
            processing.ReleaseFirst();

            await WaitHelpers.WaitUntilAsync(
                () => statistics.CompletionCount == 1,
                TimeSpan.FromSeconds(2));

            Assert.Equal(
                AutomaticProcessingOutcome.Success,
                Assert.Single(statistics.Completions).Outcome);
        });

    [Fact]
    public Task ScannerStartupFailure_IsNotAnAutomaticProcessingAttempt() =>
        StaTestRunner.RunAsync(async () =>
        {
            var statistics = new RecordingStatisticsService();
            var scanner = new FailingScanner();
            using var viewModel = CreateViewModel(
                new ImmediateProcessingService(CreateResponse(BoxProcessingStatus.Success)),
                scanner,
                statistics);

            await scanner.StartAttempted.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(
                () => viewModel.Notifications.Any(item => item.IsError),
                TimeSpan.FromSeconds(2));

            Assert.Equal(0, statistics.BeginCount);
            Assert.Empty(statistics.Completions);
        });

    [Fact]
    public Task StatisticsPersistenceFailure_DoesNotChangeProductionResult() =>
        StaTestRunner.RunAsync(async () =>
        {
            var statistics = new RecordingStatisticsService { ThrowOnCompletion = true };
            var processing = new ImmediateProcessingService(
                CreateResponse(BoxProcessingStatus.Success));
            using var viewModel = CreateViewModel(processing, new RunningScanner(), statistics);
            SetWorkModeWithoutPersistence(viewModel, WorkMode.Automatic);

            viewModel.ReceiveTenamFromScanner("4430558");

            await processing.Called.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(
                () => statistics.CompletionAttempts == 1,
                TimeSpan.FromSeconds(2));

            Assert.Equal(BoxProcessingStatus.Success, viewModel.LastProcessingStatus);
            Assert.Equal("4430558", viewModel.LastProcessedTenam);
            Assert.Equal(1, statistics.CompletionAttempts);
        });

    private static MainViewModel CreateViewModel(
        IBoxProcessingService processingService,
        IBoxScanner scanner,
        IAutomaticProcessingStatisticsService statistics) =>
        new(
            processingService,
            new NoOpWeightService(),
            scanner,
            NullLogger<MainViewModel>.Instance,
            dataSourceHealthCheck: null,
            automaticProcessingStatistics: statistics);

    private static void SetWorkModeWithoutPersistence(MainViewModel viewModel, WorkMode mode)
    {
        var field = typeof(MainViewModel).GetField(
            "_currentWorkMode",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var notify = typeof(ViewModelBase).GetMethod(
            "OnPropertyChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.NotNull(notify);
        field.SetValue(viewModel, mode);
        notify.Invoke(viewModel, [nameof(MainViewModel.CurrentWorkMode)]);
    }

    private static BoxProcessingResponse CreateResponse(BoxProcessingStatus status) =>
        new(
            status,
            status.ToString(),
            [],
            null,
            PrintPlan.None);

    private sealed class RecordingStatisticsService : IAutomaticProcessingStatisticsService
    {
        private readonly ConcurrentQueue<Completion> _completions = new();
        private int _beginCount;
        private int _completionAttempts;

        public event EventHandler? StatisticsChanged;

        public DateOnly CurrentLocalDate => new(2026, 8, 10);

        public int BeginCount => Volatile.Read(ref _beginCount);

        public int CompletionAttempts => Volatile.Read(ref _completionAttempts);

        public int CompletionCount => _completions.Count;

        public Completion[] Completions => _completions.ToArray();

        public bool ThrowOnCompletion { get; init; }

        public AutomaticProcessingAttemptContext BeginAttempt(string tenam)
        {
            Interlocked.Increment(ref _beginCount);
            return new AutomaticProcessingAttemptContext(
                Guid.NewGuid(),
                tenam,
                new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero));
        }

        public Task<bool> CompleteAttemptAsync(
            AutomaticProcessingAttemptContext attempt,
            AutomaticProcessingOutcome outcome,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _completionAttempts);

            if (ThrowOnCompletion)
            {
                throw new InvalidOperationException("SQLite unavailable");
            }

            _completions.Enqueue(new Completion(attempt, outcome));
            StatisticsChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }

        public Task<AutomaticProcessingKpiSnapshot> GetCurrentDayAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new AutomaticProcessingKpiSnapshot(
                CurrentLocalDate,
                0,
                0,
                0,
                null));
    }

    private sealed record Completion(
        AutomaticProcessingAttemptContext Attempt,
        AutomaticProcessingOutcome Outcome);

    private sealed class ImmediateProcessingService(BoxProcessingResponse response)
        : IBoxProcessingService
    {
        private readonly TaskCompletionSource<bool> _called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;

        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            _called.TrySetResult(true);
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingProcessingService : IBoxProcessingService
    {
        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Oracle failed");
    }

    private sealed class CancellableProcessingService : IBoxProcessingService
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public async Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreateResponse(BoxProcessingStatus.Success);
        }
    }

    private sealed class BlockingFirstProcessingService : IBoxProcessingService
    {
        private readonly TaskCompletionSource<bool> _firstStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _secondStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public Task FirstStarted => _firstStarted.Task;

        public Task SecondStarted => _secondStarted.Task;

        public async Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);

            if (call == 1)
            {
                _firstStarted.TrySetResult(true);
                await _releaseFirst.Task.WaitAsync(cancellationToken);
            }
            else
            {
                _secondStarted.TrySetResult(true);
            }

            return CreateResponse(BoxProcessingStatus.Success);
        }

        public void ReleaseFirst() => _releaseFirst.TrySetResult(true);
    }

    private sealed class RunningScanner : IBoxScanner
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

    private sealed class FailingScanner : IBoxScanner
    {
        private readonly TaskCompletionSource<bool> _startAttempted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived
        {
            add { }
            remove { }
        }

        public bool IsRunning => false;

        public Task StartAttempted => _startAttempted.Task;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _startAttempted.TrySetResult(true);
            throw new IOException("COM8 was not found");
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
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
}
