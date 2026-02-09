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

        // Если в твоей VM TENAM очищается на Success – раскомментируй
        // Assert.Equal(string.Empty, vm.Tenam);
    }

    [Fact]
    public async Task ScannerEvent_AutomaticallyTriggers_LoadRecords()
    {
        var scanner = new FakeScanner();
        var service = new FakeProcessingService
        {
            Response = CreateSuccessResponse(
                message: "AUTO OK",
                records: new List<LabelRecord>()
            )
        };

        var vm = new MainViewModel(service, scanner, NullLogger<MainViewModel>.Instance);

        scanner.Raise("4340559");

        await service.WaitCalledAsync();

        Assert.Equal("AUTO OK", vm.StatusMessage);
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
    }
}