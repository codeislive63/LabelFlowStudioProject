using LabelFlowStudio.Printing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LabelFlowStudio.Application.Tests.Printing;

public sealed class PrintingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLabelFlowPrinting_RegistersServices_AndBindsOptions()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Printing:DropSheetPrinterName"] = "drop",
            ["Printing:EndLabelPrinterName"] = "end",
            ["Printing:ShowDialogForDropSheet"] = "true",
            ["Printing:ShowDialogForEndLabel"] = "false",
            ["Printing:EndLabelWidthMm"] = "110",
            ["Printing:EndLabelHeightMm"] = "120"
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddLabelFlowPrinting(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PrintingOptions>>().Value;
        var builder = provider.GetService<EndLabelDocumentBuilder>();
        var printer = provider.GetService<IPrintService>();

        Assert.NotNull(builder);
        Assert.NotNull(printer);
        Assert.Equal("drop", options.DropSheetPrinterName);
        Assert.Equal("end", options.EndLabelPrinterName);
        Assert.True(options.ShowDialogForDropSheet);
        Assert.False(options.ShowDialogForEndLabel);
        Assert.Equal(110, options.EndLabelWidthMm);
        Assert.Equal(120, options.EndLabelHeightMm);
    }

    [Fact]
    public void AddLabelFlowPrinting_ThrowsValidationException_ForInvalidSizes()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Printing:EndLabelWidthMm"] = "10",
            ["Printing:EndLabelHeightMm"] = "500"
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddLabelFlowPrinting(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<PrintingOptions>>().Value);
    }
}
