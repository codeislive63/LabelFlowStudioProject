namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// Runtime-состояние доступа к Oracle, подтвержденное реальными запросами
/// текущего запуска приложения.
/// </summary>
public enum OracleConnectionState
{
    Unknown = 0,
    Checking = 1,
    Connected = 2,
    Error = 3
}
