using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task LoadRecordsCommand_LoadsRecords_UpdatesStatus()
    {
        var scanner = new FakeScanner();
        var service = new FakeProcessingService
        {
            Response = CreateSuccessResponse(
                message: "OK",
                records: new List<LabelRecord>
                {
                    new() { Tenam = "4340558", Artnr = "A", Artbez = "X", Bstmg = 1m },
                    new() { Tenam = "4340558", Artnr = "B", Artbez = "Y", Bstmg = 2m }
                }
            )
        };

        var vm = new MainViewModel(service, scanner, NullLogger<MainViewModel>.Instance)
        {
            Tenam = "4340558"
        };

        vm.LoadRecordsCommand.Execute(null);

        await service.WaitCalledAsync();

        await WaitHelpers.WaitUntilAsync(() => vm.IsBusy == false, TimeSpan.FromSeconds(2));

        Assert.Equal(2, vm.Records.Count);
        Assert.Equal("OK", vm.StatusMessage);
    }

    [Fact]
    public async Task ScannerEvent_WhenBusy_QueuesLatestTenamAndProcessesAfterCurrentRequest()
    {
        var scanner = new FakeScanner();
        var service = new DelayedProcessingService();
        var vm = new MainViewModel(service, scanner, NullLogger<MainViewModel>.Instance);

        scanner.Raise("4340551");
        await service.WaitFirstCallAsync().WaitAsync(TimeSpan.FromSeconds(2));

        scanner.Raise("4340552");

        service.ReleaseFirstCall();

        await service.WaitSecondCallAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await WaitHelpers.WaitUntilAsync(() => vm.IsBusy == false, TimeSpan.FromSeconds(2));

        Assert.Equal("4340552", service.LastRequest?.Tenam);
    }

    [Fact]
    public async Task ScannerEvent_AutomaticallyTriggers_LoadRecords()
    {
        var scanner = new FakeScanner();
        var service = new FakeProcessingService
        {
            Response = CreateSuccessResponse(
                message: "Отправлено на печать",
                records: new List<LabelRecord>()
            )
        };

        var vm = new MainViewModel(service, scanner, NullLogger<MainViewModel>.Instance);

        scanner.Raise("4340559");

        await service.WaitCalledAsync();
        await WaitHelpers.WaitUntilAsync(() => vm.IsBusy == false, TimeSpan.FromSeconds(2));

        Assert.Equal("Отправлено на печать", vm.StatusMessage);
    }


    [Fact]
    public async Task NotificationCenter_TracksUnreadAndErrorTabFiltering()
    {
        var scanner = new FakeScanner();
        var service = new ThrowingProcessingService();
        var vm = new MainViewModel(service, scanner, NullLogger<MainViewModel>.Instance)
        {
            Tenam = "4340558"
        };

        vm.LoadRecordsCommand.Execute(null);

        await WaitHelpers.WaitUntilAsync(() => vm.IsBusy == false, TimeSpan.FromSeconds(2));

        Assert.True(vm.UnreadNotificationsCount > 0);
        Assert.NotNull(vm.ToastNotification);

        vm.ToggleNotificationCenter();

        Assert.True(vm.IsNotificationCenterOpen);
        Assert.Equal(0, vm.UnreadNotificationsCount);

        vm.NotificationTabIndex = 1;

        Assert.All(vm.FilteredNotifications, notification => Assert.True(notification.IsError));
    }

    private static BoxProcessingResponse CreateSuccessResponse(string message, IReadOnlyList<LabelRecord> records)
    {
        return new BoxProcessingResponse(
            Status: BoxProcessingStatus.Success,
            Message: message,
            Records: records,
            Weight: null,
            ShouldPrintDropSheet: false,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: false
        );
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

    private sealed class FakeProcessingService : IBoxProcessingService
    {
        private readonly TaskCompletionSource<bool> _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BoxProcessingRequest? LastRequest { get; private set; }

        public BoxProcessingResponse Response { get; set; } =
            new BoxProcessingResponse(
                Status: BoxProcessingStatus.Success,
                Message: "OK",
                Records: new List<LabelRecord>(),
                Weight: null,
                ShouldPrintDropSheet: false,
                ShouldPrintEmptyDropSheet: false,
                ShouldPrintEndLabels: false
            );

        public Task<BoxProcessingResponse> ProcessAsync(BoxProcessingRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            _called.TrySetResult(true);
            return Task.FromResult(Response);
        }

        public Task WaitCalledAsync()
        {
            return _called.Task;
        }

        public Task<bool> UpdateWeightAsync(string tenam, decimal weight, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class ThrowingProcessingService : IBoxProcessingService
    {
        public Task<BoxProcessingResponse> ProcessAsync(BoxProcessingRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Ошибка сервиса");
        }

        public Task<bool> UpdateWeightAsync(string tenam, decimal weight, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class DelayedProcessingService : IBoxProcessingService
    {
        private readonly TaskCompletionSource<bool> _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _secondCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseFirstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public BoxProcessingRequest? LastRequest { get; private set; }

        public async Task<BoxProcessingResponse> ProcessAsync(BoxProcessingRequest request, CancellationToken cancellationToken)
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

            return CreateSuccessResponse("OK", new List<LabelRecord>());
        }

        public Task WaitFirstCallAsync() => _firstCall.Task;

        public Task WaitSecondCallAsync() => _secondCall.Task;

        public void ReleaseFirstCall() => _releaseFirstCall.TrySetResult(true);

        public Task<bool> UpdateWeightAsync(string tenam, decimal weight, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }
}