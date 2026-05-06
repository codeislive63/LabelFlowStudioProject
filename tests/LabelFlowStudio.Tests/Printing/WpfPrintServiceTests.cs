using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Printing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFlowStudio.Application.Tests.Printing;

public sealed class WpfPrintServiceTests
{
    [Fact]
    public void Constructor_Throws_WhenOptionsMonitorIsNull()
    {
        var builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => new WpfPrintService(null!, builder, NullLogger<WpfPrintService>.Instance));
    }

    [Fact]
    public void Constructor_Throws_WhenBuilderIsNull()
    {
        var options = new TestOptionsMonitor<PrintingOptions>(new PrintingOptions());

        Assert.Throws<ArgumentNullException>(() => new WpfPrintService(options, null!, NullLogger<WpfPrintService>.Instance));
    }

    [Fact]
    public void Constructor_Throws_WhenLoggerIsNull()
    {
        var options = new TestOptionsMonitor<PrintingOptions>(new PrintingOptions());
        var builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => new WpfPrintService(options, builder, null!));
    }

    [Fact]
    public async Task PrintDropSheetAsync_Throws_WhenResponseIsNull()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.PrintDropSheetAsync(null!, "4340558", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PrintDropSheetAsync_Throws_WhenTenamIsEmpty(string tenam)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.PrintDropSheetAsync(CreateResponse(), tenam, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PrintEmptyDropSheetAsync_Throws_WhenTenamIsEmpty(string tenam)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.PrintEmptyDropSheetAsync(tenam, CancellationToken.None));
    }

    [Fact]
    public async Task PrintEndLabelAsync_Throws_WhenResponseIsNull()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.PrintEndLabelAsync(null!, "4340558", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PrintEndLabelAsync_Throws_WhenTenamIsEmpty(string tenam)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.PrintEndLabelAsync(CreateResponse(), tenam, CancellationToken.None));
    }

    private static WpfPrintService CreateService()
    {
        var options = new PrintingOptions();
        var optionsMonitor = new TestOptionsMonitor<PrintingOptions>(options);

        return new WpfPrintService(optionsMonitor, CreateBuilder(), NullLogger<WpfPrintService>.Instance);
    }

    private static EndLabelDocumentBuilder CreateBuilder()
    {
        return new EndLabelDocumentBuilder(new TestOptionsMonitor<PrintingOptions>(new PrintingOptions()));
    }

    private static BoxProcessingResponse CreateResponse()
    {
        return new BoxProcessingResponse(
            Status: BoxProcessingStatus.Success,
            Message: "ok",
            Records: [],
            Weight: 1m,
            PrintPlan: PrintPlan.None);
    }
}
