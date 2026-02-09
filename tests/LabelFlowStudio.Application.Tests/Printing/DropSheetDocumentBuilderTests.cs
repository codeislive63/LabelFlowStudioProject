using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Printing;

namespace LabelFlowStudio.Application.Tests.Printing;

public sealed class DropSheetDocumentBuilderTests
{
    private const string Tenam = "4340558";

    [Fact]
    public void BuildEmpty_ReturnsDocument()
    {
        var doc = StaTestRunner.Run(() => DropSheetDocumentBuilder.BuildEmpty(Tenam));

        Assert.NotNull(doc);
    }

    [Fact]
    public void Build_CreatesDocument_WithRecords()
    {
        var records = new List<LabelRecord>
        {
            new()
            {
                Tenam = "4340558",
                Artnr = "111",
                Artbez = "Test product 1",
                Bstmg = 1m,
                Aufid = "AUF-1",
                Gpbez = "Place",
                Gport1 = "City",
                Gpstrasse = "Street 1"
            },
            new()
            {
                Tenam = "4340558",
                Artnr = "222",
                Artbez = "Test product 2",
                Bstmg = 2m,
                Aufid = "AUF-1",
                Gpbez = "Place",
                Gport1 = "City",
                Gpstrasse = "Street 1"
            }
        };

        var response = new BoxProcessingResponse(
            Status: BoxProcessingStatus.Success,
            Message: "OK",
            Records: records,
            Weight: 10m,
            ShouldPrintDropSheet: false,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: false
        );

        var doc = StaTestRunner.Run(() => DropSheetDocumentBuilder.Build(response, Tenam));

        Assert.NotNull(doc);
    }
}
