using System.Diagnostics;
using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public Task LoadRecordsCommand_LoadsRecords_UpdatesStatus() =>
        StaTestRunner.RunAsync(async () =>
        {
            var scanner = new FakeScanner();
            var service = new FakeProcessingService
            {
                Response = CreateSuccessResponse(
                    message: "OK",
                    records:
                    [
                        new() { Tenam = "4340558", Artnr = "A", Artbez = "X", Bstmg = 1m },
                        new() { Tenam = "4340558", Artnr = "B", Artbez = "Y", Bstmg = 2m }
                    ])
            };

            var vm = CreateViewModel(service, scanner);
            vm.Tenam = "4340558";

            vm.LoadRecordsCommand.Execute(null);

            await service.WaitCalledAsync();
            await WaitHelpers.WaitUntilAsync(() => vm.IsBusy == false, TimeSpan.FromSeconds(2));

            Assert.Equal(2, vm.Records.Count);
            Assert.Equal("OK", vm.StatusMessage);
        });

    [Fact]
    public void LoadRecordsCommand_DoesNotBlockCallingThread_WhileProcessingStarts()
    {
        StaTestRunner.Run(() =>
        {
            var scanner = new FakeScanner();
            var service = new BlockingProcessingService(TimeSpan.FromMilliseconds(250));
            var vm = CreateViewModel(service, scanner);
            vm.Tenam = "4340558";

            var stopwatch = Stopwatch.StartNew();
            vm.LoadRecordsCommand.Execute(null);
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(150), $"Execute blocked for {stopwatch.Elapsed}.");
            Assert.True(vm.IsBusy);
        });
    }

    [Fact]
    public Task ScannerEvent_WhenBusy_QueuesLatestTenamAndProcessesAfterCurrentRequest() =>
        StaTestRunner.RunAsync(async () =>
        {
            var scanner = new FakeScanner();
            var service = new DelayedProcessingService();
            var vm = CreateViewModel(service, scanner);

            scanner.Raise("4340551");
            await service.WaitFirstCallAsync().WaitAsync(TimeSpan.FromSeconds(2));

            scanner.Raise("4340552");

            service.ReleaseFirstCall();

            await service.WaitSecondCallAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(() => vm.IsBusy == false, TimeSpan.FromSeconds(2));

            Assert.Equal("4340552", service.LastRequest?.Tenam);
        });

    [Fact]
    public Task ScannerEvent_AutomaticallyTriggers_LoadRecords() =>
        StaTestRunner.RunAsync(async () =>
        {
            var scanner = new FakeScanner();
            var service = new FakeProcessingService
            {
                Response = CreateSuccessResponse(
                    message: "Отправлено на печать",
                    records: [])
            };

            var vm = CreateViewModel(service, scanner);

            scanner.Raise("4340559");

            await service.WaitCalledAsync();
            await WaitHelpers.WaitUntilAsync(() => vm.IsBusy == false, TimeSpan.FromSeconds(2));

            Assert.Equal("4340559", service.LastRequest?.Tenam);
            Assert.Equal("4340559", vm.LastProcessedTenam);
        });

    [Fact]
    public Task NotificationCenter_TracksUnreadAndErrorTabFiltering() =>
        StaTestRunner.RunAsync(async () =>
        {
            var scanner = new FakeScanner();
            var service = new ThrowingProcessingService();
            var vm = CreateViewModel(service, scanner);
            vm.Tenam = "4340558";

            vm.LoadRecordsCommand.Execute(null);

            await WaitHelpers.WaitUntilAsync(() => vm.IsBusy == false, TimeSpan.FromSeconds(2));

            Assert.True(vm.UnreadNotificationsCount > 0);
            Assert.True(vm.HasUnreadErrorNotifications);
            Assert.NotEmpty(vm.Notifications);

            var unreadBeforeOpen = vm.UnreadNotificationsCount;

            vm.ToggleNotificationCenter();

            Assert.True(vm.IsNotificationCenterOpen);
            Assert.Equal(unreadBeforeOpen, vm.UnreadNotificationsCount);

            vm.NotificationTabIndex = 1;

            Assert.All(vm.FilteredNotificationsView.Cast<UiNotification>(), notification => Assert.True(notification.IsError));

            vm.MarkNotificationAsRead(vm.SelectedNotification);

            Assert.Equal(Math.Max(0, unreadBeforeOpen - 1), vm.UnreadNotificationsCount);
        });

    [Fact]
    public void NotificationCenter_WarningTabReflectsNewWarnings()
    {
        StaTestRunner.Run(() =>
        {
            var scanner = new FakeScanner();
            var service = new FakeProcessingService();
            var vm = CreateViewModel(service, scanner);

            vm.NotificationTabIndex = 2;

            var addNotification = typeof(MainViewModel).GetMethod(
                "AddNotification",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.NotNull(addNotification);

            addNotification!.Invoke(vm, ["Тестовое предупреждение", NotificationCategory.Warning]);

            var filtered = vm.FilteredNotificationsView.Cast<UiNotification>().ToList();

            Assert.Single(filtered);
            Assert.True(filtered[0].IsWarning);
            Assert.Same(filtered[0], vm.SelectedNotification);
        });
    }

    [Fact]
    public Task LoadRecordsCommand_AddsNotificationForSuccessfulProcessing() =>
        StaTestRunner.RunAsync(async () =>
        {
            var scanner = new FakeScanner();
            var service = new FakeProcessingService
            {
                Response = CreateSuccessResponse(
                    message: "Данные загружены",
                    records:
                    [
                        new() { Tenam = "4340558", Artnr = "A", Artbez = "X", Bstmg = 1m }
                    ])
            };

            var vm = CreateViewModel(service, scanner);
            vm.Tenam = "4340558";

            vm.LoadRecordsCommand.Execute(null);

            await service.WaitCalledAsync();
            await WaitHelpers.WaitUntilAsync(() => vm.IsBusy == false, TimeSpan.FromSeconds(2));

            Assert.NotEmpty(vm.Notifications);
            Assert.Equal("Короб №4340558: Данные загружены", vm.Notifications[0].Message);
        });

    [Fact]
    public Task NotificationCenter_ShowsToastForErrorsToo() =>
        StaTestRunner.RunAsync(async () =>
        {
            var scanner = new FakeScanner();
            var service = new ThrowingProcessingService();
            var vm = CreateViewModel(service, scanner);
            vm.Tenam = "4340558";

            vm.LoadRecordsCommand.Execute(null);

            await WaitHelpers.WaitUntilAsync(() => vm.IsBusy == false, TimeSpan.FromSeconds(2));

            Assert.NotEmpty(vm.Notifications);
            Assert.True(vm.Notifications[0].IsError);
            Assert.Contains("Ошибка сервиса", vm.Notifications[0].Message);
        });

    private static MainViewModel CreateViewModel(
        IBoxProcessingService processingService,
        IBoxScanner scanner,
        IBoxWeightService? weightService = null)
    {
        return new MainViewModel(
            processingService,
            weightService ?? new FakeBoxWeightService(),
            scanner,
            NullLogger<MainViewModel>.Instance);
    }

    private static BoxProcessingResponse CreateSuccessResponse(
        string message,
        IReadOnlyList<LabelRecord> records,
        PrintPlan? printPlan = null)
    {
        return new BoxProcessingResponse(
            Status: BoxProcessingStatus.Success,
            Message: message,
            Records: records,
            Weight: null,
            PrintPlan: printPlan ?? PrintPlan.None);
    }

    private sealed class FakeScanner : IBoxScanner, IDisposable
    {
        public event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived;

        public bool IsRunning => true;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Raise(string tenam)
        {
            BoxNumberReceived?.Invoke(this, new BoxNumberReceivedEventArgs(tenam));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeBoxWeightService : IBoxWeightService
    {
        public Task<BoxWeightUpdateResult> UpdateWeightAsync(
            string tenam,
            decimal weight,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(BoxWeightUpdateResult.Success());
        }
    }

    private sealed class FakeProcessingService : IBoxProcessingService
    {
        private readonly TaskCompletionSource<bool> _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BoxProcessingRequest? LastRequest { get; private set; }

        public BoxProcessingResponse Response { get; set; } =
            new(
                Status: BoxProcessingStatus.Success,
                Message: "OK",
                Records: [],
                Weight: null,
                PrintPlan: PrintPlan.None);

        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            _called.TrySetResult(true);
            return Task.FromResult(Response);
        }

        public Task WaitCalledAsync()
        {
            return _called.Task;
        }
    }

    private sealed class ThrowingProcessingService : IBoxProcessingService
    {
        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Ошибка сервиса");
        }
    }

    private sealed class DelayedProcessingService : IBoxProcessingService
    {
        private readonly TaskCompletionSource<bool> _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _secondCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseFirstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public BoxProcessingRequest? LastRequest { get; private set; }

        public async Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            _callCount++;

            if (_callCount == 1)
            {
                _firstCall.TrySetResult(true);
                await _releaseFirstCall.Task;
            }
            else if (_callCount == 2)
            {
                _secondCall.TrySetResult(true);
            }

            return CreateSuccessResponse("OK", []);
        }

        public Task WaitFirstCallAsync() => _firstCall.Task;

        public Task WaitSecondCallAsync() => _secondCall.Task;

        public void ReleaseFirstCall() => _releaseFirstCall.TrySetResult(true);
    }

    private sealed class BlockingProcessingService : IBoxProcessingService
    {
        private readonly TimeSpan _delay;

        public BlockingProcessingService(TimeSpan delay)
        {
            _delay = delay;
        }

        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            Thread.Sleep(_delay);
            return Task.FromResult(CreateSuccessResponse("OK", []));
        }
    }
}
