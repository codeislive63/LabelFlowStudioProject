using LabelFlowStudio.Devices.BoxScanner;

namespace LabelFlowStudio.Application.Tests.Devices;

public sealed class BoxNumberReceivedEventArgsTests
{
    [Fact]
    public void Ctor_SetsBoxNumber_AndReceivedAtUtc()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var args = new BoxNumberReceivedEventArgs("4340558");

        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.Equal("4340558", args.BoxNumber);
        Assert.InRange(args.ReceivedAt, before, after);
    }
}