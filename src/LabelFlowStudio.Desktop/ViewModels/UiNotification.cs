namespace LabelFlowStudio.Desktop.ViewModels;

public sealed class UiNotification
{
    public UiNotification(DateTime timestamp, string message, bool isError)
    {
        Timestamp = timestamp;
        Message = message;
        IsError = isError;
    }

    public DateTime Timestamp { get; }

    public string Message { get; }

    public bool IsError { get; }
}
