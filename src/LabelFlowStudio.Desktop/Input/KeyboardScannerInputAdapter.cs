namespace LabelFlowStudio.Desktop.Input;

/// <summary>
/// Режим перехвата ввода от клавиатурного сканера.
/// </summary>
public enum KeyboardScannerInputMode
{
    Manual,
    Automatic
}

/// <summary>
/// Тип клавиши, значимый для распознавания скана.
/// </summary>
public enum KeyboardScannerKeyKind
{
    Other,
    Enter
}

/// <summary>
/// Результат обработки одного события клавиатурного ввода.
/// </summary>
public enum KeyboardScannerInputOutcome
{
    Ignored,
    Consumed,
    BufferUpdated,
    BufferCleared,
    ScanCompleted
}

/// <summary>
/// Решение адаптера для вызывающего WPF behavior.
/// </summary>
/// <param name="Outcome">Результат изменения состояния адаптера.</param>
/// <param name="ShouldHandle">Нужно ли пометить исходное WPF-событие как обработанное.</param>
/// <param name="Buffer">Актуальное содержимое буфера после обработки события.</param>
/// <param name="CompletedScan">Завершённый скан или <see langword="null"/>.</param>
public readonly record struct KeyboardScannerInputResult(
    KeyboardScannerInputOutcome Outcome,
    bool ShouldHandle,
    string Buffer,
    string? CompletedScan = null);

/// <summary>
/// WPF-независимая машина состояний для keyboard-wedge сканера.
/// </summary>
/// <remarks>
/// Решение о перехвате ввода при фокусе в редактируемом элементе остаётся у вызывающего
/// behavior и передаётся через <c>shouldCapture</c>. Время события передаётся явно,
/// поэтому класс не зависит от системных часов и DispatcherTimer.
/// </remarks>
public sealed class KeyboardScannerInputAdapter
{
    public static readonly TimeSpan DefaultBufferTimeout = TimeSpan.FromMilliseconds(900);
    public static readonly TimeSpan DefaultAutomaticMaxInterKeyDelay = TimeSpan.FromMilliseconds(60);

    private readonly TimeSpan _bufferTimeout;
    private readonly TimeSpan _automaticMaxInterKeyDelay;

    private string _buffer = string.Empty;
    private DateTimeOffset? _lastBufferedInputAtUtc;
    private DateTimeOffset? _lastAutomaticDigitAtUtc;

    public KeyboardScannerInputAdapter(
        TimeSpan? bufferTimeout = null,
        TimeSpan? automaticMaxInterKeyDelay = null)
    {
        _bufferTimeout = bufferTimeout ?? DefaultBufferTimeout;
        _automaticMaxInterKeyDelay = automaticMaxInterKeyDelay ?? DefaultAutomaticMaxInterKeyDelay;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_bufferTimeout, TimeSpan.Zero, nameof(bufferTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _automaticMaxInterKeyDelay,
            TimeSpan.Zero,
            nameof(automaticMaxInterKeyDelay));
    }

    /// <summary>
    /// Таймаут неактивного буфера. Behavior может использовать его для своего UI-таймера.
    /// </summary>
    public TimeSpan BufferTimeout => _bufferTimeout;

    /// <summary>
    /// Максимальная пауза между цифровыми событиями в автоматическом режиме.
    /// </summary>
    public TimeSpan AutomaticMaxInterKeyDelay => _automaticMaxInterKeyDelay;

    /// <summary>
    /// Текущее содержимое буфера.
    /// </summary>
    public string Buffer => _buffer;

    /// <summary>
    /// Обрабатывает текстовое событие клавиатуры.
    /// </summary>
    public KeyboardScannerInputResult ProcessTextInput(
        string? text,
        KeyboardScannerInputMode mode,
        DateTimeOffset occurredAtUtc,
        bool shouldCapture = true)
    {
        if (!shouldCapture)
        {
            return Result(KeyboardScannerInputOutcome.Ignored, shouldHandle: false);
        }

        var bufferExpired = ExpireIfNeeded(occurredAtUtc);

        if (!IsDigitsOnly(text))
        {
            if (mode == KeyboardScannerInputMode.Automatic)
            {
                return Result(
                    bufferExpired ? KeyboardScannerInputOutcome.BufferCleared : KeyboardScannerInputOutcome.Consumed,
                    shouldHandle: true);
            }

            return Result(
                bufferExpired ? KeyboardScannerInputOutcome.BufferCleared : KeyboardScannerInputOutcome.Ignored,
                shouldHandle: false);
        }

        if (mode == KeyboardScannerInputMode.Automatic
            && _lastAutomaticDigitAtUtc is { } lastAutomaticDigitAtUtc
            && occurredAtUtc - lastAutomaticDigitAtUtc > _automaticMaxInterKeyDelay)
        {
            // Сохраняем исходную временную метку до общего 900-ms timeout. Это повторяет
            // текущую защиту: после слишком медленной клавиши остаток той же
            // последовательности не должен превратиться в валидный укороченный TENAM.
            _buffer = string.Empty;
            return Result(KeyboardScannerInputOutcome.BufferCleared, shouldHandle: true);
        }

        if (mode == KeyboardScannerInputMode.Automatic)
        {
            _lastAutomaticDigitAtUtc = occurredAtUtc;
        }

        _buffer += text;
        _lastBufferedInputAtUtc = occurredAtUtc;

        return Result(KeyboardScannerInputOutcome.BufferUpdated, shouldHandle: true);
    }

    /// <summary>
    /// Обрабатывает нажатие Enter или другой управляющей клавиши.
    /// </summary>
    public KeyboardScannerInputResult ProcessKeyDown(
        KeyboardScannerKeyKind keyKind,
        KeyboardScannerInputMode mode,
        DateTimeOffset occurredAtUtc,
        bool shouldCapture = true)
    {
        if (!shouldCapture)
        {
            return Result(KeyboardScannerInputOutcome.Ignored, shouldHandle: false);
        }

        var bufferExpired = ExpireIfNeeded(occurredAtUtc);

        if (keyKind != KeyboardScannerKeyKind.Enter)
        {
            if (mode == KeyboardScannerInputMode.Automatic)
            {
                return Result(
                    bufferExpired ? KeyboardScannerInputOutcome.BufferCleared : KeyboardScannerInputOutcome.Consumed,
                    shouldHandle: true);
            }

            return Result(
                bufferExpired ? KeyboardScannerInputOutcome.BufferCleared : KeyboardScannerInputOutcome.Ignored,
                shouldHandle: false);
        }

        if (_buffer.Length == 0)
        {
            return Result(
                bufferExpired ? KeyboardScannerInputOutcome.BufferCleared : KeyboardScannerInputOutcome.Consumed,
                shouldHandle: mode == KeyboardScannerInputMode.Automatic);
        }

        var completedScan = _buffer;
        ClearCore();

        return Result(
            KeyboardScannerInputOutcome.ScanCompleted,
            shouldHandle: true,
            completedScan);
    }

    /// <summary>
    /// Очищает буфер, если с последнего принятого текста прошёл таймаут.
    /// </summary>
    public KeyboardScannerInputResult ExpireBuffer(DateTimeOffset occurredAtUtc)
    {
        return ExpireIfNeeded(occurredAtUtc)
            ? Result(KeyboardScannerInputOutcome.BufferCleared, shouldHandle: false)
            : Result(KeyboardScannerInputOutcome.Ignored, shouldHandle: false);
    }

    /// <summary>
    /// Безусловно сбрасывает текущее состояние распознавания.
    /// </summary>
    public KeyboardScannerInputResult Reset()
    {
        var hadBufferedInput = _buffer.Length > 0 || _lastAutomaticDigitAtUtc is not null;
        ClearCore();

        return Result(
            hadBufferedInput ? KeyboardScannerInputOutcome.BufferCleared : KeyboardScannerInputOutcome.Ignored,
            shouldHandle: false);
    }

    private static bool IsDigitsOnly(string? text)
    {
        return !string.IsNullOrEmpty(text) && text.All(char.IsDigit);
    }

    private bool ExpireIfNeeded(DateTimeOffset occurredAtUtc)
    {
        if (_lastBufferedInputAtUtc is not { } lastBufferedInputAtUtc
            || occurredAtUtc - lastBufferedInputAtUtc < _bufferTimeout)
        {
            return false;
        }

        ClearCore();
        return true;
    }

    private void ClearCore()
    {
        _buffer = string.Empty;
        _lastBufferedInputAtUtc = null;
        _lastAutomaticDigitAtUtc = null;
    }

    private KeyboardScannerInputResult Result(
        KeyboardScannerInputOutcome outcome,
        bool shouldHandle,
        string? completedScan = null)
    {
        return new KeyboardScannerInputResult(outcome, shouldHandle, _buffer, completedScan);
    }
}
