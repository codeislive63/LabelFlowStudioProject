namespace LabelFlowStudio.Devices.BoxScanner;

/// <summary>
/// Заглушка сканера коробов для режима без подключенного COM-устройства
/// </summary>
public sealed class NullBoxScanner : IBoxScanner
{
    /// <summary>
    /// Событие получения номера короба
    /// </summary>
    public event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Возвращает признак запущенного сканера
    /// </summary>
    public bool IsRunning => false;

    /// <summary>
    /// Запускает заглушку сканера
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Останавливает заглушку сканера
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Освобождает ресурсы заглушки сканера
    /// </summary>
    public void Dispose()
    {
    }
}
