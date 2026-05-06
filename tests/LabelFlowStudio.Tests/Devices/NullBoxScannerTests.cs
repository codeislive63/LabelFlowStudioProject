using LabelFlowStudio.Devices.BoxScanner;

namespace LabelFlowStudio.Application.Tests.Devices;

public sealed class NullBoxScannerTests
{
    [Fact]
    public async Task StartStopAndDispose_DoNotThrow_AndIsRunningIsFalse()
    {
        using var scanner = new NullBoxScanner();

        await scanner.StartAsync(CancellationToken.None);
        await scanner.StopAsync(CancellationToken.None);

        Assert.False(scanner.IsRunning);
    }
}
