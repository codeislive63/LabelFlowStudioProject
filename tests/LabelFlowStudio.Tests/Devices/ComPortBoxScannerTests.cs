using System.Reflection;
using System.Text;
using System.IO.Ports;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFlowStudio.Application.Tests.Devices;

public sealed class ComPortBoxScannerTests
{
    [Fact]
    public void ProcessBufferLocked_ParsesLinesAndRaisesEvents()
    {
        var options = new BoxScannerOptions
        {
            PortName = "COM1",
            LineSeparator = "\n"
        };

        var scanner = CreateScanner(options);
        var (buffer, method, snapshotField) = GetPrivateMembers(scanner);

        snapshotField.SetValue(scanner, options);
        buffer.Append("4340558\n4340559\n");

        var received = new List<string>();
        scanner.BoxNumberReceived += (_, e) => received.Add(e.BoxNumber);

        method.Invoke(scanner, null);

        Assert.Equal(new[] { "4340558", "4340559" }, received);
    }

    [Fact]
    public void ProcessBufferLocked_UsesFallbackNewLine_WhenCustomSeparatorNotFound()
    {
        var options = new BoxScannerOptions
        {
            PortName = "COM1",
            LineSeparator = "\r\n"
        };

        var scanner = CreateScanner(options);
        var (buffer, method, snapshotField) = GetPrivateMembers(scanner);

        snapshotField.SetValue(scanner, options);
        buffer.Append("111\n");

        string? received = null;
        scanner.BoxNumberReceived += (_, e) => received = e.BoxNumber;

        method.Invoke(scanner, null);

        Assert.Equal("111", received);
    }

    [Fact]
    public void ProcessBufferLocked_SkipsWhitespaceLines()
    {
        var options = new BoxScannerOptions
        {
            PortName = "COM1",
            LineSeparator = "\n"
        };

        var scanner = CreateScanner(options);
        var (buffer, method, snapshotField) = GetPrivateMembers(scanner);

        snapshotField.SetValue(scanner, options);
        buffer.Append("   \n222\n\n");

        var received = new List<string>();
        scanner.BoxNumberReceived += (_, e) => received.Add(e.BoxNumber);

        method.Invoke(scanner, null);

        Assert.Equal(new[] { "222" }, received);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenPortNameIsMissing()
    {
        var scanner = CreateScanner(new BoxScannerOptions { PortName = " " });

        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_ThrowsObjectDisposedException_AfterDispose()
    {
        var scanner = CreateScanner(new BoxScannerOptions { PortName = "COM1" });

        scanner.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => scanner.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_DisposesCreatedPort_WhenOpenFails()
    {
        var options = new BoxScannerOptions { PortName = "COM1" };
        var port = new TrackingSerialPort("__LABELFLOW_MISSING_PORT__");
        var scanner = new ComPortBoxScanner(
            new TestOptionsMonitor<BoxScannerOptions>(options),
            NullLogger<ComPortBoxScanner>.Instance,
            _ => port);

        await Assert.ThrowsAnyAsync<Exception>(() => scanner.StartAsync(CancellationToken.None));

        Assert.True(port.IsDisposed);
        Assert.False(scanner.IsRunning);
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_DoesNothing()
    {
        var scanner = CreateScanner(new BoxScannerOptions { PortName = "COM1" });

        await scanner.StopAsync(CancellationToken.None);

        Assert.False(scanner.IsRunning);
    }

    [Fact]
    public void TrimBufferIfNeededLocked_TrimsLargeBuffer()
    {
        var options = new BoxScannerOptions
        {
            PortName = "COM1",
            LineSeparator = "\n"
        };

        var scanner = CreateScanner(options);
        var bufferField = typeof(ComPortBoxScanner).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance);
        var trimMethod = typeof(ComPortBoxScanner).GetMethod("TrimBufferIfNeededLocked", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(bufferField);
        Assert.NotNull(trimMethod);

        var buffer = (StringBuilder)bufferField!.GetValue(scanner)!;
        buffer.Append(new string('1', 5000));

        trimMethod!.Invoke(scanner, null);

        Assert.True(buffer.Length <= 4096);
    }

    private static ComPortBoxScanner CreateScanner(BoxScannerOptions options)
    {
        return new ComPortBoxScanner(new TestOptionsMonitor<BoxScannerOptions>(options), NullLogger<ComPortBoxScanner>.Instance);
    }

    private static (StringBuilder Buffer, MethodInfo Method, FieldInfo SnapshotField) GetPrivateMembers(ComPortBoxScanner scanner)
    {
        var bufferField = typeof(ComPortBoxScanner).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance);
        var snapshotField = typeof(ComPortBoxScanner).GetField("_optionsSnapshot", BindingFlags.NonPublic | BindingFlags.Instance);
        var method = typeof(ComPortBoxScanner).GetMethod("ProcessBufferLocked", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(bufferField);
        Assert.NotNull(snapshotField);
        Assert.NotNull(method);

        var buffer = (StringBuilder)bufferField!.GetValue(scanner)!;

        return (buffer, method!, snapshotField!);
    }

    private sealed class TrackingSerialPort(string portName) : SerialPort(portName)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
