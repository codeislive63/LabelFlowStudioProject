namespace LabelFlowStudio.Desktop.ViewModels;

public sealed class UiNotification
{
    public UiNotification(DateTime timestamp, string message, NotificationCategory category)
    {
        Timestamp = timestamp;
        Message = message;
        Category = category;
    }

    public DateTime Timestamp { get; }

    public string Message { get; }

    public NotificationCategory Category { get; }

    public bool IsError => Category == NotificationCategory.Error;

    public bool IsWarning => Category == NotificationCategory.Warning;

    public bool IsSuccess => Category == NotificationCategory.Success;

    public string CategoryText => Category switch
    {
        NotificationCategory.Error => "Ошибка",
        NotificationCategory.Warning => "Предупреждение",
        NotificationCategory.Success => "Успех",
        _ => "Уведомление"
    };

    public string CategoryIcon => Category switch
    {
        NotificationCategory.Error => "⛔",
        NotificationCategory.Warning => "⚠",
        NotificationCategory.Success => "✔",
        _ => "•"
    };
}

public enum NotificationCategory
{
    Success = 0,
    Warning = 1,
    Error = 2
}
