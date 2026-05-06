using LabelFlowStudio.Templates;

namespace LabelFlowStudio.Application.Tests.Templates;

public sealed class TemplateDefaultsTests
{
    [Fact]
    public void GetEndLabelHtml_ReturnsTemplateText()
    {
        var html = TemplateDefaults.GetEndLabelHtml();

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{{Tenam}}", html, StringComparison.Ordinal);
    }

    [Fact]
    public void GetStuffingSheetHtml_ReturnsTemplateText()
    {
        var html = TemplateDefaults.GetStuffingSheetHtml();

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{{Tenam}}", html, StringComparison.Ordinal);
        Assert.Contains("{% for", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetEmptyPageHtml_ReturnsTemplateText()
    {
        var html = TemplateDefaults.GetEmptyPageHtml();

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
    }
}
