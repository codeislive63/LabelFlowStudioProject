namespace LabelFlowStudio.Devices.BoxScanner;

public sealed class BoxNumberReceivedEventArgs : EventArgs
{
    public BoxNumberReceivedEventArgs(string boxNumber)
    {
        BoxNumber = boxNumber;
        ReceivedAt = DateTimeOffset.UtcNow;
    }

    public string BoxNumber { get; }

    public DateTimeOffset ReceivedAt { get; }
}
