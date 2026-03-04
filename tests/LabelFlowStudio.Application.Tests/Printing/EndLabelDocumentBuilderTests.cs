using System.Windows;
using System.Windows.Controls;
using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Printing;

namespace LabelFlowStudio.Application.Tests.Printing;

public sealed class EndLabelDocumentBuilderTests
{
    [Fact]
    public void Constructor_Throws_WhenOptionsMonitorIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new EndLabelDocumentBuilder(null!));
    }

    [Fact]
    public void Build_Throws_WhenResponseIsNull()
    {
        var builder = CreateBuilder();

        AssertStaThrows<ArgumentNullException>(() => builder.Build(null!, "4340558"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Build_Throws_WhenTenamIsEmpty(string tenam)
    {
        var builder = CreateBuilder();
        var response = CreateResponse(weight: 10m);

        AssertStaThrows<ArgumentException>(() => builder.Build(response, tenam));
    }

    [Fact]
    public void Build_CreatesDocument_AndUsesWeightText()
    {
        var builder = CreateBuilder();
        var response = CreateResponse(weight: 12.345m);

        var (pagesCount, textBlocks) = StaTestRunner.Run(() =>
        {
            var document = builder.Build(response, "4340558");
            var extracted = ExtractTextBlocks(document);

            return (document.Pages.Count, extracted);
        });

        Assert.Equal(1, pagesCount);
        Assert.Contains(textBlocks, text => text == "TENAM 4340558");
        Assert.Contains(textBlocks, text => text == "Вес 12.345");
    }

    [Fact]
    public void Build_UsesMissingWeightMessage_WhenWeightIsNull()
    {
        var builder = CreateBuilder();
        var response = CreateResponse(weight: null);

        var textBlocks = StaTestRunner.Run(() =>
        {
            var document = builder.Build(response, "4340558");
            return ExtractTextBlocks(document);
        });

        Assert.Contains(textBlocks, text => text == "Вес отсутствует");
    }

    private static void AssertStaThrows<TException>(Action action) where TException : Exception
    {
        var aggregateException = Assert.Throws<AggregateException>(() => StaTestRunner.Run(action));

        Assert.IsType<TException>(aggregateException.InnerException);
    }

    private static EndLabelDocumentBuilder CreateBuilder(double width = 100, double height = 150)
    {
        var options = new PrintingOptions
        {
            EndLabelWidthMm = width,
            EndLabelHeightMm = height
        };

        return new EndLabelDocumentBuilder(new TestOptionsMonitor<PrintingOptions>(options));
    }

    private static BoxProcessingResponse CreateResponse(decimal? weight)
    {
        return new BoxProcessingResponse(
            Status: BoxProcessingStatus.Success,
            Message: "OK",
            Records: Array.Empty<LabelFlowStudio.Core.Models.LabelRecord>(),
            Weight: weight,
            ShouldPrintDropSheet: false,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: true);
    }

    private static List<string> ExtractTextBlocks(System.Windows.Documents.FixedDocument document)
    {
        var result = new List<string>();

        foreach (var pageContent in document.Pages)
        {
            if (pageContent is not System.Windows.Documents.PageContent content)
            {
                continue;
            }

            content.Child?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            content.Child?.Arrange(new Rect(content.Child.DesiredSize));

            var page = content.Child as System.Windows.Documents.FixedPage;
            var stack = page?.Children.OfType<StackPanel>().FirstOrDefault();

            if (stack is null)
            {
                continue;
            }

            result.AddRange(stack.Children.OfType<TextBlock>().Select(x => x.Text));
        }

        return result;
    }
}
