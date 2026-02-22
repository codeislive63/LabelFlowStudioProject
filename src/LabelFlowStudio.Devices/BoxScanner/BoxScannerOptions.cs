using System.ComponentModel.DataAnnotations;
using System.IO.Ports;

namespace LabelFlowStudio.Devices.BoxScanner;

public sealed class BoxScannerOptions
{
    public bool IsEnabled { get; set; } = true;

    public string PortName { get; set; } = "COM8";

    [Range(1200, 115200)]
    public int BaudRate { get; set; } = 9600;

    [Range(5, 8)]
    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    public Handshake Handshake { get; set; } = Handshake.None;

    public string LineSeparator { get; set; } = "\n";

    [Range(50, 10000)]
    public int ReadTimeoutMilliseconds { get; set; } = 500;
}
