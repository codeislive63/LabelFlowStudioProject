using System.Reflection;
using System.Text;
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

        var scanner = new ComPortBoxScanner(new TestOptionsMonitor<BoxScannerOptions>(options), NullLogger<ComPortBoxScanner>.Instance);

        var bufferField = typeof(ComPortBoxScanner).GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance);
        var snapshotField = typeof(ComPortBoxScanner).GetField("_optionsSnapshot", BindingFlags.NonPublic | BindingFlags.Instance);
        var method = typeof(ComPortBoxScanner).GetMethod("ProcessBufferLocked", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(bufferField);
        Assert.NotNull(snapshotField);
        Assert.NotNull(method);

        snapshotField!.SetValue(scanner, options);

        var sb = (StringBuilder)bufferField!.GetValue(scanner)!;
        sb.Append("4340558\n4340559\n");

        var received = new List<string>();

        scanner.BoxNumberReceived += (_, e) => received.Add(e.BoxNumber);

        method!.Invoke(scanner, null);

        Assert.Equal(new[] { "4340558", "4340559" }, received);
    }
}
