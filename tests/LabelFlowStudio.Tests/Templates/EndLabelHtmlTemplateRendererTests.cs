using System.Text.RegularExpressions;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Templates;

namespace LabelFlowStudio.Application.Tests.Templates;

public sealed class EndLabelHtmlTemplateRendererTests
{
    [Fact]
    public void Render_ReplacesTokens_AndGeneratesBarcode()
    {
        var records = new[]
        {
            new LabelRecord
            {
                Tenam = "4340558",
                Lfakdnr = "001",
                Gpbez = "ACME",
                Gport1 = "Minsk",
                Gpstrasse = "Main st 1",
                Bstchgnam5 = "ORDER-123",
                CountBst = 2m,
                SumBst = 10m
            }
        };

        var response = new BoxProcessingResponse(
            Status: BoxProcessingStatus.Success,
            Message: "ok",
            Records: records,
            Weight: 6.325m,
            PrintPlan: new PrintPlan(
                PrintDropSheet: true,
                PrintEmptyDropSheet: false,
                PrintEndLabels: true));

        var template = string.Join("\n", new[]
        {
            "TENAM={{Tenam}}",
            "SHOP={{Lfakdnr}}",
            "NAME={{Gpbez}}",
            "CITY={{DeliveryCity}}",
            "STREET={{DeliveryStreet}}",
            "ORDER={{Bstchgnam5}}",
            "BRUTTO={{Brutto}}",
            "SUM={{SumBst}}",
            "BAR={{BarcodeDataUri}}"
        });

        var html = StaTestRunner.Run(() => EndLabelHtmlTemplateRenderer.Render(template, response, tenam: "4340558"));

        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
        Assert.Contains("TENAM=4340558", html, StringComparison.Ordinal);
        Assert.Contains("BRUTTO=6.325", html, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", html, StringComparison.Ordinal);

        Assert.False(Regex.IsMatch(html, @"\{\{\s*BarcodeDataUri\s*\}\}", RegexOptions.CultureInvariant));
    }

    [Fact]
    public void Render_HtmlEncodesFieldValues()
    {
        var records = new[]
        {
            new LabelRecord
            {
                Tenam = "4340558",
                Gpbez = "A&B <C>",
                Gport1 = "X",
                Gpstrasse = "Y",
                Lfakdnr = "001",
                Bstchgnam5 = "ORDER"
            }
        };

        var response = new BoxProcessingResponse(
            Status: BoxProcessingStatus.Success,
            Message: "ok",
            Records: records,
            Weight: 1.0m,
            PrintPlan: new PrintPlan(
                PrintDropSheet: true,
                PrintEmptyDropSheet: false,
                PrintEndLabels: true));

        var template = "NAME={{Gpbez}}";

        var html = StaTestRunner.Run(() => EndLabelHtmlTemplateRenderer.Render(template, response, tenam: "4340558"));

        Assert.Contains("NAME=A&amp;B &lt;C&gt;", html, StringComparison.Ordinal);
    }
}
