using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.Tests.BoxProcessing;

public sealed class BoxProcessingContractsTests
{
    [Fact]
    public void BoxProcessingRequest_StoresValues()
    {
        var request = new BoxProcessingRequest(
            Tenam: "T001",
            Mode: WorkMode.Automatic,
            ShouldPrintEndLabels: true,
            ShouldPrintStuffingSheet: false,
            UseScales: true);

        Assert.Equal("T001", request.Tenam);
        Assert.Equal(WorkMode.Automatic, request.Mode);
        Assert.True(request.ShouldPrintEndLabels);
        Assert.False(request.ShouldPrintStuffingSheet);
        Assert.True(request.UseScales);
    }

    [Fact]
    public void BoxProcessingResponse_StoresValues()
    {
        var records = new List<LabelRecord>
        {
            new() { Tenam = "4340558", Artnr = "PN-1", CountBst = 1 }
        };

        var printPlan = new PrintPlan(
            PrintDropSheet: true,
            PrintEmptyDropSheet: false,
            PrintEndLabels: true);

        var response = new BoxProcessingResponse(
            Status: BoxProcessingStatus.Success,
            Message: "ok",
            Records: records,
            Weight: 4.5m,
            PrintPlan: printPlan);

        Assert.Equal(BoxProcessingStatus.Success, response.Status);
        Assert.Equal("ok", response.Message);
        Assert.Single(response.Records);
        Assert.Equal(4.5m, response.Weight);
        Assert.Equal(printPlan, response.PrintPlan);
        Assert.True(response.PrintPlan.PrintDropSheet);
        Assert.False(response.PrintPlan.PrintEmptyDropSheet);
        Assert.True(response.PrintPlan.PrintEndLabels);
    }

    [Fact]
    public void PrintPlan_None_HasNoPrintActions()
    {
        var printPlan = PrintPlan.None;

        Assert.False(printPlan.PrintDropSheet);
        Assert.False(printPlan.PrintEmptyDropSheet);
        Assert.False(printPlan.PrintEndLabels);
        Assert.True(printPlan.IsEmpty);
    }
}
