using System.ComponentModel.DataAnnotations;
using System.IO.Ports;
using LabelFlowStudio.Devices.BoxScanner;

namespace LabelFlowStudio.Application.Tests.Devices;

public sealed class BoxScannerOptionsTests
{
    [Fact]
    public void Defaults_AreExpected()
    {
        var options = new BoxScannerOptions();

        Assert.True(options.IsEnabled);
        Assert.Equal("COM5", options.PortName);
        Assert.Equal(9600, options.BaudRate);
        Assert.Equal(8, options.DataBits);
        Assert.Equal(Parity.None, options.Parity);
        Assert.Equal(StopBits.One, options.StopBits);
        Assert.Equal(Handshake.None, options.Handshake);
        Assert.Equal("\n", options.LineSeparator);
        Assert.Equal(500, options.ReadTimeoutMilliseconds);
    }

    [Fact]
    public void Validation_FailsForOutOfRangeValues()
    {
        var options = new BoxScannerOptions
        {
            BaudRate = 100,
            DataBits = 3,
            ReadTimeoutMilliseconds = 1
        };

        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.True(results.Count >= 3);
    }
}
