using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelFlowStudio.Devices.BoxScanner;

public sealed class ComPortBoxScanner : IBoxScanner
{
    private readonly IOptionsMonitor<BoxScannerOptions> _optionsMonitor;
    private readonly ILogger<ComPortBoxScanner> _logger;

    private readonly object _sync = new();
    private readonly StringBuilder _buffer = new();

    private SerialPort? _serialPort;
    private bool _disposed;

    private BoxScannerOptions _optionsSnapshot = new();

    public ComPortBoxScanner(IOptionsMonitor<BoxScannerOptions> optionsMonitor, ILogger<ComPortBoxScanner> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
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

        var options = _optionsMonitor.CurrentValue;

        if (string.IsNullOrWhiteSpace(options.PortName))
        {
            throw new InvalidOperationException("Box scanner PortName is not configured");
        }

        try
        {
            _optionsSnapshot = options;

            _serialPort = new SerialPort(options.PortName, options.BaudRate)
            {
                DataBits = options.DataBits,
                Parity = options.Parity,
                StopBits = options.StopBits,
                Handshake = options.Handshake,
                ReadTimeout = options.ReadTimeoutMilliseconds
            };

            _serialPort.DataReceived += OnDataReceived;
            _serialPort.Open();

            IsRunning = true;

            _logger.LogInformation("Box scanner started on {PortName}", options.PortName);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to start box scanner on {PortName}", options.PortName);

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
        var separator = _optionsSnapshot.LineSeparator;
        
        if (string.IsNullOrEmpty(separator))
        {
            separator = "\n";
        }

        while (true)
        {
            var bufferText = _buffer.ToString();
            var separatorIndex = bufferText.IndexOf(separator, StringComparison.Ordinal);

            if (separatorIndex < 0 && separator != "\n")
            {
                separatorIndex = bufferText.IndexOf("\n", StringComparison.Ordinal);
                
                if (separatorIndex >= 0)
                {
                    separator = "\n";
                }
            }

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
