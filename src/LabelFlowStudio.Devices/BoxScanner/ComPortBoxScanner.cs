using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelFlowStudio.Devices.BoxScanner;

public sealed class ComPortBoxScanner : IBoxScanner
{
    private readonly BoxScannerOptions _options;
    private readonly ILogger<ComPortBoxScanner> _logger;

    private readonly object _sync = new();
    private readonly StringBuilder _buffer = new();

    private SerialPort? _serialPort;
    private bool _disposed;

    public ComPortBoxScanner(IOptions<BoxScannerOptions> options, ILogger<ComPortBoxScanner> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived;

    public bool IsRunning { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(_options.PortName))
        {
            throw new InvalidOperationException("Box scanner PortName is not configured");
        }

        try
        {
            _serialPort = new SerialPort(_options.PortName, _options.BaudRate)
            {
                DataBits = _options.DataBits,
                Parity = _options.Parity,
                StopBits = _options.StopBits,
                Handshake = _options.Handshake,
                ReadTimeout = _options.ReadTimeoutMilliseconds
            };

            _serialPort.DataReceived += OnDataReceived;
            _serialPort.Open();

            IsRunning = true;

            _logger.LogInformation("Box scanner started on {PortName}", _options.PortName);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start box scanner on {PortName}", _options.PortName);

            CleanupPort();
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!IsRunning)
        {
            return Task.CompletedTask;
        }

        IsRunning = false;

        try
        {
            CleanupPort();
            _logger.LogInformation("Box scanner stopped");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to stop box scanner");
        }

        return Task.CompletedTask;
    }

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
            // при dispose не падаем
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
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
                ProcessBufferLocked();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Box scanner read failed");
        }
    }

    private void ProcessBufferLocked()
    {
        var separator = _options.LineSeparator;
        
        if (string.IsNullOrEmpty(separator))
        {
            separator = "\r\n";
        }

        while (true)
        {
            var bufferText = _buffer.ToString();
            var separatorIndex = bufferText.IndexOf(separator, StringComparison.Ordinal);

            if (separatorIndex < 0)
            {
                return;
            }

            var line = bufferText.Substring(0, separatorIndex);
            _buffer.Remove(0, separatorIndex + separator.Length);

            var boxNumber = (line ?? string.Empty).Trim();
            
            if (string.IsNullOrWhiteSpace(boxNumber))
            {
                continue;
            }

            _logger.LogInformation("Box number received: {BoxNumber}", boxNumber);
            BoxNumberReceived?.Invoke(this, new BoxNumberReceivedEventArgs(boxNumber));
        }
    }

    private void CleanupPort()
    {
        var serialPort = _serialPort;
        
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
        
        _serialPort = null;

        lock (_sync)
        {
            _buffer.Clear();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ComPortBoxScanner));
        }
    }
}
