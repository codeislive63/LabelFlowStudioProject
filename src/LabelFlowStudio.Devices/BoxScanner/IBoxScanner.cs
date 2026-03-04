namespace LabelFlowStudio.Devices.BoxScanner;

/// <summary>
/// Контракт сканера коробов
/// </summary>
public interface IBoxScanner : IDisposable
{
    /// <summary>
    /// Событие получения номера короба
    /// </summary>
    event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived;

    /// <summary>
    /// Признак активного состояния сканера
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Запускает сканер
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Останавливает сканер
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);
}
