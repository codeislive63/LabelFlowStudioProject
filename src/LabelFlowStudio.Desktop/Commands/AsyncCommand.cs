using System.Windows.Input;

namespace LabelFlowStudio.Desktop.Commands;

/// <summary>
/// Команда для безопасного запуска асинхронных операций из UI
/// </summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onException;

    private bool _isExecuting;

    public AsyncCommand(Func<Task> executeAsync, Func<bool>? canExecute = null, Action<Exception>? onException = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
        _onException = onException;
    }

    /// <summary>
    /// Событие изменения доступности команды
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Проверяет возможность выполнения команды
    /// </summary>
    public bool CanExecute(object? parameter)
    {
        if (_isExecuting)
        {
            return false;
        }

        if (_canExecute is null)
        {
            return true;
        }

        return _canExecute();
    }

    /// <summary>
    /// Запускает выполнение команды
    /// </summary>
    public void Execute(object? parameter)
    {
        _ = ExecuteInternalAsync(parameter);
    }

    // Выполняет команду с централизованной обработкой ошибок
    private async Task ExecuteInternalAsync(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();

            await _executeAsync();
        }
        catch (Exception exception)
        {
            _onException?.Invoke(exception);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Оповещает UI об изменении возможности выполнения команды
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
