using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.Tests.BoxProcessing;

public sealed class BoxProcessingContractsTests
{
    [Fact]
    public void BoxProcessingRequest_StoresValues()
    {
        var request = new BoxProcessingRequest("T001", WorkMode.Automatic, true, false);

        Assert.Equal("T001", request.Tenam);
        Assert.Equal(WorkMode.Automatic, request.Mode);
        Assert.True(request.ShouldPrintEndLabels);
        Assert.False(request.ShouldPrintStuffingSheet);
    }

    [Fact]
    public void BoxProcessingResponse_StoresValues()
    {
        var records = new List<LabelRecord>
        {
            new() { Tenam = "4340558", Artnr = "PN-1", CountBst = 1 }
        };

        var response = new BoxProcessingResponse(
            BoxProcessingStatus.Success,
            "ok",
            records,
            Weight: 4.5m,
            ShouldPrintDropSheet: true,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: true
        );

        Assert.Equal(BoxProcessingStatus.Success, response.Status);
        Assert.Equal("ok", response.Message);
        Assert.Single(response.Records);
        Assert.Equal(4.5m, response.Weight);
        Assert.True(response.ShouldPrintDropSheet);
        Assert.False(response.ShouldPrintEmptyDropSheet);
        Assert.True(response.ShouldPrintEndLabels);
    }
}
