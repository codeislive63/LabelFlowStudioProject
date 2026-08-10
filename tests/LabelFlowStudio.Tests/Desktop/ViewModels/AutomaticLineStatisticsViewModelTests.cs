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

public sealed class AutomaticLineStatisticsViewModelTests
{
    [Fact]
    public Task RefreshStatistics_ProjectsPersistedCurrentDayKpis() =>
        StaTestRunner.RunAsync(async () =>
        {
            var localDate = new DateOnly(2026, 8, 10);
            var statistics = new FakeStatisticsService
            {
                CurrentLocalDate = localDate,
                Snapshot = new AutomaticProcessingKpiSnapshot(
                    localDate,
                    CompletedCount: 7,
                    SuccessCount: 5,
                    ErrorCount: 1,
                    BoxesPerHour: 84.3)
            };

            using var work = CreateWorkViewModel();
            using var automatic = CreateAutomaticViewModel(work, statistics);

            await automatic.RefreshStatisticsAsync();

            Assert.Equal("7", automatic.ShiftCompletedValueText);
            Assert.Equal("5", automatic.ShiftSuccessValueText);
            Assert.Equal("1", automatic.ShiftErrorValueText);
            Assert.Equal("84", automatic.ShiftSpeedValueText);
            Assert.Equal("за сегодня", automatic.ShiftCompletedCaptionText);
            Assert.Equal("кор/ч", automatic.ShiftSpeedCaptionText);
            Assert.Equal(1, statistics.ReadCount);
        });

    [Fact]
    public Task StatisticsChanged_RefreshesKpisAndAValidEmptyDayShowsZeroCounts() =>
        StaTestRunner.RunAsync(async () =>
        {
            var localDate = new DateOnly(2026, 8, 10);
            var statistics = new FakeStatisticsService
            {
                CurrentLocalDate = localDate,
                Snapshot = new AutomaticProcessingKpiSnapshot(localDate, 1, 1, 0, null)
            };

            using var work = CreateWorkViewModel();
            using var automatic = CreateAutomaticViewModel(work, statistics);
            await automatic.RefreshStatisticsAsync();

            var nextDate = localDate.AddDays(1);
            statistics.CurrentLocalDate = nextDate;
            statistics.Snapshot = new AutomaticProcessingKpiSnapshot(nextDate, 0, 0, 0, null);
            statistics.RaiseStatisticsChanged();

            await WaitHelpers.WaitUntilAsync(
                () => statistics.ReadCount >= 2 && automatic.ShiftCompletedValueText == "0",
                TimeSpan.FromSeconds(2));

            Assert.Equal("0", automatic.ShiftCompletedValueText);
            Assert.Equal("0", automatic.ShiftSuccessValueText);
            Assert.Equal("0", automatic.ShiftErrorValueText);
            Assert.Equal("–", automatic.ShiftSpeedValueText);
            Assert.Equal("Недостаточно данных", automatic.ShiftSpeedCaptionText);
        });

    [Fact]
    public Task LocalDayCheck_OnlyReadsAgainAfterTheCalendarDateChanges() =>
        StaTestRunner.RunAsync(async () =>
        {
            var localDate = new DateOnly(2026, 8, 10);
            var statistics = new FakeStatisticsService
            {
                CurrentLocalDate = localDate,
                Snapshot = new AutomaticProcessingKpiSnapshot(localDate, 3, 2, 1, 12)
            };

            using var work = CreateWorkViewModel();
            using var automatic = CreateAutomaticViewModel(work, statistics);
            await automatic.RefreshStatisticsAsync();
            await automatic.RefreshStatisticsIfLocalDayChangedAsync();

            Assert.Equal(1, statistics.ReadCount);

            statistics.CurrentLocalDate = localDate.AddDays(1);
            statistics.Snapshot = new AutomaticProcessingKpiSnapshot(
                statistics.CurrentLocalDate,
                0,
                0,
                0,
                null);

            await automatic.RefreshStatisticsIfLocalDayChangedAsync();

            Assert.Equal(2, statistics.ReadCount);
            Assert.Equal("0", automatic.ShiftCompletedValueText);
        });

    [Fact]
    public Task FailedStatisticsRead_UsesAnExplicitUnavailableState() =>
        StaTestRunner.RunAsync(async () =>
        {
            var statistics = new FakeStatisticsService
            {
                ThrowOnRead = true
            };

            using var work = CreateWorkViewModel();
            using var automatic = CreateAutomaticViewModel(work, statistics);

            await automatic.RefreshStatisticsAsync();

            Assert.Equal("–", automatic.ShiftCompletedValueText);
            Assert.Equal("Недоступно", automatic.ShiftCompletedCaptionText);
            Assert.Equal("–", automatic.ShiftSpeedValueText);
            Assert.Equal("Недоступно", automatic.ShiftSpeedCaptionText);
        });

    private static AutomaticLineViewModel CreateAutomaticViewModel(
        MainViewModel work,
        IAutomaticProcessingStatisticsService statistics) =>
        new(
            work,
            () => new AutomaticLineEquipmentSnapshot(
                IsScannerRunning: true,
                IsPrinterInstalled: false,
                UseScales: false),
            statisticsService: statistics);

    private static MainViewModel CreateWorkViewModel() =>
        new(
            new NoOpProcessingService(),
            new NoOpWeightService(),
            new FakeScanner(),
            NullLogger<MainViewModel>.Instance);

    private sealed class FakeStatisticsService : IAutomaticProcessingStatisticsService
    {
        public event EventHandler? StatisticsChanged;

        public DateOnly CurrentLocalDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        public AutomaticProcessingKpiSnapshot Snapshot { get; set; } = new(
            DateOnly.FromDateTime(DateTime.Today),
            0,
            0,
            0,
            null);

        public bool ThrowOnRead { get; set; }

        public int ReadCount { get; private set; }

        public AutomaticProcessingAttemptContext BeginAttempt(string tenam) =>
            new(Guid.NewGuid(), tenam, DateTimeOffset.UtcNow);

        public Task<bool> CompleteAttemptAsync(
            AutomaticProcessingAttemptContext context,
            AutomaticProcessingOutcome outcome,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<AutomaticProcessingKpiSnapshot> GetCurrentDayAsync(
            CancellationToken cancellationToken = default)
        {
            ReadCount++;

            return ThrowOnRead
                ? Task.FromException<AutomaticProcessingKpiSnapshot>(
                    new InvalidOperationException("SQLite unavailable"))
                : Task.FromResult(Snapshot);
        }

        public void RaiseStatisticsChanged() => StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class NoOpProcessingService : IBoxProcessingService
    {
        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BoxProcessingResponse(
                BoxProcessingStatus.Success,
                "OK",
                [],
                null,
                PrintPlan.None));
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
