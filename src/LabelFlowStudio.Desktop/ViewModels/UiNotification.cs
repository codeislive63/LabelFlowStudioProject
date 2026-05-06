using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LabelFlowStudio.Desktop.ViewModels;

public sealed class UiNotification : INotifyPropertyChanged
{
    private bool _isRead;

    public UiNotification(DateTime timestamp, string message, NotificationCategory category)
    {
        Timestamp = timestamp;
        Message = message;
        Category = category;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DateTime Timestamp { get; }

    public string Message { get; }

    public NotificationCategory Category { get; }

    public bool IsRead
    {
        get => _isRead;
        private set
        {
            if (_isRead == value)
            {
                return;
            }

            _isRead = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUnread));
        }
    }

    public bool IsUnread => !IsRead;

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

    public void MarkAsRead()
    {
        IsRead = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum NotificationCategory
{
    Success = 0,
    Warning = 1,
    Error = 2
}
