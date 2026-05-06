using LabelFlowStudio.Desktop.Templates;

namespace LabelFlowStudio.Application.Tests.Templates;

public sealed class TemplateStorePathTests
{
    [Fact]
    public void EndLabelTemplateStorePath_ContainsExpectedFileName()
    {
        var path = EndLabelTemplateStore.GetTemplatePath();

        Assert.EndsWith(Path.Combine("Templates", "EndLabel.html"), path);
    }

    [Fact]
    public void StuffingSheetTemplateStorePath_ContainsExpectedFileName()
    {
        var path = StuffingSheetTemplateStore.GetTemplatePath();

        Assert.EndsWith(Path.Combine("Templates", "StuffingSheet.html"), path);
    }

    [Fact]
    public void EmptyPageTemplateStorePath_ContainsExpectedFileName()
    {
        var path = EmptyPageTemplateStore.GetTemplatePath();

        Assert.EndsWith(Path.Combine("Templates", "EmptyPage.html"), path);
    }
}
