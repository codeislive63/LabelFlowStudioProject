using System.IO.Ports;

namespace LabelFlowStudio.Devices.BoxScanner;

public sealed class BoxScannerOptions
{
    public string PortName { get; set; } = string.Empty;

    public int BaudRate { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    public Handshake Handshake { get; set; } = Handshake.None;

    public string LineSeparator { get; set; } = "\r\n";

    public int ReadTimeoutMilliseconds { get; set; } = 500;
}
