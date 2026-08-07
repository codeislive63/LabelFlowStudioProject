namespace LabelFlowStudio.Core.Abstractions;

/// <summary>
/// Проверяет доступность основного источника данных приложения.
/// </summary>
public interface IDataSourceHealthCheck
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
