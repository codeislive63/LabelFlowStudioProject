namespace LabelFlowStudio.Devices.BoxScanner;

public sealed class NullBoxScanner : IBoxScanner
{
    public event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived;

    public bool IsRunning => false;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
