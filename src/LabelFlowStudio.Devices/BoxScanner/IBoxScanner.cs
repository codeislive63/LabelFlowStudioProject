namespace LabelFlowStudio.Devices.BoxScanner;

public interface IBoxScanner : IDisposable
{
    event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived;

    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
