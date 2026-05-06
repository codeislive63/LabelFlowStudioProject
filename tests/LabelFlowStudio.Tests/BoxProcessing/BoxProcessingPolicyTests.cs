using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Policies;

namespace LabelFlowStudio.Tests.BoxProcessing;

public sealed class BoxProcessingPolicyTests
{
    [Fact]
    public void CreateSuccessPrintPlan_RequestIsNull_ThrowsArgumentNullException()
    {
        var policy = new BoxProcessingPolicy();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            policy.CreateSuccessPrintPlan(null!));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public void CreateMissingWeightPrintPlan_RequestIsNull_ThrowsArgumentNullException()
    {
        var policy = new BoxProcessingPolicy();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            policy.CreateMissingWeightPrintPlan(null!));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public void CreateSuccessPrintPlan_AllPrintOptionsEnabled_ReturnsDropSheetAndEndLabels()
    {
        var policy = new BoxProcessingPolicy();

        var plan = policy.CreateSuccessPrintPlan(CreateRequest(
            shouldPrintEndLabels: true,
            shouldPrintStuffingSheet: true));

        Assert.Equal(
            new PrintPlan(
                PrintDropSheet: true,
                PrintEmptyDropSheet: false,
                PrintEndLabels: true),
            plan);
    }

    [Fact]
    public void CreateSuccessPrintPlan_AllPrintOptionsDisabled_ReturnsEmptyPlan()
    {
        var policy = new BoxProcessingPolicy();

        var plan = policy.CreateSuccessPrintPlan(CreateRequest(
            shouldPrintEndLabels: false,
            shouldPrintStuffingSheet: false));

        Assert.Equal(PrintPlan.None, plan);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void CreateMissingWeightPrintPlan_ManualMode_ReturnsEmptyPlan()
    {
        var policy = new BoxProcessingPolicy();

        var plan = policy.CreateMissingWeightPrintPlan(CreateRequest(
            mode: WorkMode.Manual,
            shouldPrintEndLabels: true,
            shouldPrintStuffingSheet: true,
            useScales: false));

        Assert.Equal(PrintPlan.None, plan);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void CreateMissingWeightPrintPlan_AutomaticModeWithScales_ReturnsEmptyPlan()
    {
        var policy = new BoxProcessingPolicy();

        var plan = policy.CreateMissingWeightPrintPlan(CreateRequest(
            mode: WorkMode.Automatic,
            shouldPrintEndLabels: true,
            shouldPrintStuffingSheet: true,
            useScales: true));

        Assert.Equal(PrintPlan.None, plan);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void CreateMissingWeightPrintPlan_AutomaticModeWithoutScalesAndStuffingSheetEnabled_ReturnsEmptyDropSheetPlan()
    {
        var policy = new BoxProcessingPolicy();

        var plan = policy.CreateMissingWeightPrintPlan(CreateRequest(
            mode: WorkMode.Automatic,
            shouldPrintEndLabels: true,
            shouldPrintStuffingSheet: true,
            useScales: false));

        Assert.Equal(
            new PrintPlan(
                PrintDropSheet: false,
                PrintEmptyDropSheet: true,
                PrintEndLabels: false),
            plan);
    }

    [Fact]
    public void CreateMissingWeightPrintPlan_AutomaticModeWithoutScalesAndStuffingSheetDisabled_ReturnsEmptyPlan()
    {
        var policy = new BoxProcessingPolicy();

        var plan = policy.CreateMissingWeightPrintPlan(CreateRequest(
            mode: WorkMode.Automatic,
            shouldPrintEndLabels: true,
            shouldPrintStuffingSheet: false,
            useScales: false));

        Assert.Equal(PrintPlan.None, plan);
        Assert.True(plan.IsEmpty);
    }

    private static BoxProcessingRequest CreateRequest(
        WorkMode mode = WorkMode.Manual,
        bool shouldPrintEndLabels = true,
        bool shouldPrintStuffingSheet = true,
        bool useScales = false)
    {
        return new BoxProcessingRequest(
            Tenam: "4340558",
            Mode: mode,
            ShouldPrintEndLabels: shouldPrintEndLabels,
            ShouldPrintStuffingSheet: shouldPrintStuffingSheet,
            UseScales: useScales
        );
    }
}
