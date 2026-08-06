using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelFlowStudio.Devices.BoxScanner;

/// <summary>
/// Сканер коробов через COM порт
/// </summary>
public sealed class ComPortBoxScanner : IBoxScanner
{
    private const string DefaultLineSeparator = "\n";
    private const int MaxBufferLength = 4096;

    private readonly IOptionsMonitor<BoxScannerOptions> _optionsMonitor;
    private readonly ILogger<ComPortBoxScanner> _logger;
    private readonly Func<BoxScannerOptions, SerialPort> _serialPortFactory;

    private readonly object _sync = new();
    private readonly object _lifecycleSync = new();
    private readonly StringBuilder _buffer = new();

    private SerialPort? _serialPort;
    private bool _disposed;

    private BoxScannerOptions _optionsSnapshot = new();

    /// <summary>
    /// Создает экземпляр сканера COM порта
    /// </summary>
    public ComPortBoxScanner(IOptionsMonitor<BoxScannerOptions> optionsMonitor, ILogger<ComPortBoxScanner> logger)
        : this(optionsMonitor, logger, BuildSerialPort)
    {
    }

    internal ComPortBoxScanner(
        IOptionsMonitor<BoxScannerOptions> optionsMonitor,
        ILogger<ComPortBoxScanner> logger,
        Func<BoxScannerOptions, SerialPort> serialPortFactory)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serialPortFactory = serialPortFactory ?? throw new ArgumentNullException(nameof(serialPortFactory));
    }

    /// <summary>
    /// Событие получения номера короба
    /// </summary>
    public event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived;

    /// <summary>
    /// Признак активного состояния сканера
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Запускает чтение данных из COM порта
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var options = _optionsMonitor.CurrentValue;

        if (string.IsNullOrWhiteSpace(options.PortName))
        {
            throw new InvalidOperationException("Box scanner PortName is not configured");
        }

        SerialPort? startedPort = null;

        try
        {
            lock (_lifecycleSync)
            {
                if (IsRunning)
                {
                    return Task.CompletedTask;
                }

                CleanupPort();

                _optionsSnapshot = options;

                var port = _serialPortFactory(options)
                           ?? throw new InvalidOperationException("Serial port factory returned null");

                try
                {
                    port.DataReceived += OnDataReceived;
                    port.Open();
                }
                catch
                {
                    CleanupDetachedPort(port);
                    throw;
                }

                _serialPort = port;
                startedPort = port;
                IsRunning = true;

                _logger.LogInformation("Box scanner started on {PortName}", options.PortName);
            }

            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start box scanner on {PortName}", options.PortName);

            lock (_lifecycleSync)
            {
                if (_serialPort is null || ReferenceEquals(_serialPort, startedPort))
                {
                    IsRunning = false;
                    CleanupPort();
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Останавливает чтение данных из COM порта
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            lock (_lifecycleSync)
            {
                if (!IsRunning && _serialPort is null)
                {
                    return Task.CompletedTask;
                }

                IsRunning = false;
                CleanupPort();

                _logger.LogInformation("Box scanner stopped");
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to stop box scanner");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Освобождает ресурсы сканера
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    // Создает и настраивает экземпляр SerialPort
    private static SerialPort BuildSerialPort(BoxScannerOptions options)
    {
        return new SerialPort(options.PortName, options.BaudRate)
        {
            DataBits = options.DataBits,
            Parity = options.Parity,
            StopBits = options.StopBits,
            Handshake = options.Handshake,
            ReadTimeout = options.ReadTimeoutMilliseconds
        };
    }

    // Читает данные из COM порта и дописывает их в буфер
    private void OnDataReceived(object sender, SerialDataReceivedEventArgs eventArgs)
    {
        try
        {
            var serialPort = _serialPort;

            if (serialPort is null)
            {
                return;
            }

            var received = serialPort.ReadExisting();

            if (string.IsNullOrEmpty(received))
            {
                return;
            }

            lock (_sync)
            {
                _buffer.Append(received);
                TrimBufferIfNeededLocked();
                ProcessBufferLocked();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Box scanner read failed");
        }
    }

    // Извлекает завершенные строки из буфера и публикует событие
    private void ProcessBufferLocked()
    {
        var separator = ResolveSeparator(_optionsSnapshot.LineSeparator);

        while (TryReadNextLine(separator, out var boxNumber))
        {
            if (string.IsNullOrWhiteSpace(boxNumber))
            {
                continue;
            }

            _logger.LogInformation("Box number received: {BoxNumber}", boxNumber);
            BoxNumberReceived?.Invoke(this, new BoxNumberReceivedEventArgs(boxNumber));
        }
    }

    // Возвращает корректный разделитель строк
    private static string ResolveSeparator(string? configuredSeparator)
    {
        return string.IsNullOrEmpty(configuredSeparator)
            ? DefaultLineSeparator
            : configuredSeparator;
    }

    // Пытается извлечь очередную строку из буфера
    private bool TryReadNextLine(string separator, out string line)
    {
        line = string.Empty;

        var separatorIndex = IndexOf(_buffer, separator);
        var separatorLength = separator.Length;

        if (separatorIndex < 0 && separator != DefaultLineSeparator)
        {
            separatorIndex = IndexOf(_buffer, DefaultLineSeparator);
            separatorLength = DefaultLineSeparator.Length;
        }

        if (separatorIndex < 0)
        {
            return false;
        }

        line = _buffer.ToString(0, separatorIndex).Trim();
        _buffer.Remove(0, separatorIndex + separatorLength);

        return true;
    }

    // Находит индекс подстроки в StringBuilder без лишних аллокаций
    private static int IndexOf(StringBuilder source, string value)
    {
        if (source.Length == 0 || string.IsNullOrEmpty(value) || value.Length > source.Length)
        {
            return -1;
        }

        for (var i = 0; i <= source.Length - value.Length; i++)
        {
            var matched = true;

            for (var j = 0; j < value.Length; j++)
            {
                if (source[i + j] == value[j])
                {
                    continue;
                }

                matched = false;
                break;
            }

            if (matched)
            {
                return i;
            }
        }

        return -1;
    }

    // Освобождает ресурсы SerialPort и очищает буфер
    private void CleanupPort()
    {
        var serialPort = _serialPort;
        _serialPort = null;

        if (serialPort is null)
        {
            return;
        }

        try
        {
            serialPort.DataReceived -= OnDataReceived;
        }
        catch
        {
        }

        try
        {
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }
        catch
        {
        }

        serialPort.Dispose();

        lock (_sync)
        {
            _buffer.Clear();
        }
    }

    // Освобождает порт, владение которым еще не было передано полю _serialPort
    private void CleanupDetachedPort(SerialPort serialPort)
    {
        try
        {
            serialPort.DataReceived -= OnDataReceived;
        }
        catch
        {
        }

        try
        {
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }
        catch
        {
        }

        try
        {
            serialPort.Dispose();
        }
        catch
        {
        }
    }

    // Ограничивает размер входного буфера, чтобы исключить неограниченный рост памяти
    private void TrimBufferIfNeededLocked()
    {
        if (_buffer.Length <= MaxBufferLength)
        {
            return;
        }

        var charsToRemove = _buffer.Length - MaxBufferLength;
        _buffer.Remove(0, charsToRemove);

        _logger.LogWarning(
            "Box scanner input buffer exceeded {MaxBufferLength} chars and was trimmed by {TrimmedChars}",
            MaxBufferLength,
            charsToRemove);
    }

    // Проверяет что экземпляр не освобожден
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ComPortBoxScanner));
        }
    }
}
