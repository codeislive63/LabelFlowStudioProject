using System.Text.RegularExpressions;
using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Application.Tests.TestData;
using LabelFlowStudio.Desktop.Templates;

namespace LabelFlowStudio.Application.Tests.Templates;

public sealed class StuffingSheetHtmlTemplateRendererTests
{
    [Fact]
    public void Render_ReplacesHeaderTokens_RendersLoop_AndGeneratesBarcode()
    {
        var records = LabelRecordTestData.LoadByTenam("4340558");

        var response = new BoxProcessingResponse(
            BoxProcessingStatus.Success,
            Message: "ok",
            Records: records,
            Weight: null,
            ShouldPrintDropSheet: true,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: true
        );

        var template = string.Join("\n", new[]
        {
            "COUNTRY={{Lndnam}}",
            "TENAM={{Tenam}}",
            "ADDR={{Gpplz}}|{{Gpbez}}|{{Gport1}}|{{Gpstrasse}}",
            "AUF={{Aufid}}",
            "DATE={{CurrentDate}} TIME={{CurrentTime}}",
            "SUM={{SumBst}}",
            "BAR={{BarcodeDataUri}}",
            "{% for record in Records %}{{RowNumber}} {{Artnr}} {{Artbez}} {{Bstmg}};{% endfor %}"
        });

        var html = StaTestRunner.Run(() => StuffingSheetHtmlTemplateRenderer.Render(template, response, tenam: "4340558"));

        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
        Assert.Contains("TENAM=4340558", html, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", html, StringComparison.Ordinal);

        Assert.Matches(new Regex(@"DATE=\d{2}\.\d{2}\.\d{4} TIME=\d{2}:\d{2}:\d{2}", RegexOptions.CultureInvariant), html);

        Assert.Matches(new Regex(@"\b1\b", RegexOptions.CultureInvariant), html);
        Assert.Contains(records[0].Artnr, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IsBackwardCompatibleWithLegacyTokenNames()
    {
        var records = LabelRecordTestData.LoadByTenam("4340559");

        var response = new BoxProcessingResponse(
            BoxProcessingStatus.Success,
            Message: "ok",
            Records: records,
            Weight: null,
            ShouldPrintDropSheet: true,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: true
        );

        var template = string.Join("\n", new[]
        {
            "country={{ country }} te={{ te }}",
            "index={{ index }} place={{ place }} city={{ city }} street={{ street }}",
            "aufid={{ aufid }} date={{ current_date }} time={{ current_time }}",
            "sum={{ sum }} barcode={{ barcode }}",
            "{% for product in products %}{{ product.ROWNUM }} {{ product.ARTNR }} {{ product.ARTBEZ }} {{ product.BSTMG }};{% endfor %}"
        });

        var html = StaTestRunner.Run(() => StuffingSheetHtmlTemplateRenderer.Render(template, response, tenam: "4340559"));

        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
        Assert.Contains("te=4340559", html, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", html, StringComparison.Ordinal);
        Assert.Contains(records[0].Artnr, html, StringComparison.Ordinal);
    }


    [Fact]
    public void Render_PaginatesRecords_AndReplacesPageTokens()
    {
        var records = Enumerable.Range(1, 45)
            .Select(index => new LabelFlowStudio.Core.Models.LabelRecord
            {
                Lndnam = "RU",
                Tenam = "4340558",
                Artnr = $"ART-{index:000}",
                Artbez = $"Name {index:000}",
                Bstmg = index
            })
            .ToArray();

        var response = new BoxProcessingResponse(
            BoxProcessingStatus.Success,
            Message: "ok",
            Records: records,
            Weight: null,
            ShouldPrintDropSheet: true,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: true
        );

        const string template = "<html><head></head><body>{% for record in Records %}<div>{{RowNumber}} {{Artnr}}</div>{% endfor %}<footer>{{CurrentPage}}/{{TotalPages}}</footer></body></html>";

        var html = StaTestRunner.Run(() => StuffingSheetHtmlTemplateRenderer.Render(template, response, tenam: "4340558"));

        Assert.Equal(2, Regex.Matches(html, "<section class=\"lfs-sheet-page\">", RegexOptions.CultureInvariant).Count);
        Assert.Contains("<footer>1/2</footer>", html, StringComparison.Ordinal);
        Assert.Contains("<footer>2/2</footer>", html, StringComparison.Ordinal);
        Assert.Contains("<div>45 ART-045</div>", html, StringComparison.Ordinal);
    }


    [Fact]
    public void Render_UsesConservativePageCapacity_ToAvoidOrphanFooterPage()
    {
        var records = Enumerable.Range(1, 41)
            .Select(index => new LabelFlowStudio.Core.Models.LabelRecord
            {
                Lndnam = "RU",
                Tenam = "4340558",
                Artnr = $"ART-{index:000}",
                Artbez = $"Name {index:000}",
                Bstmg = index
            })
            .ToArray();

        var response = new BoxProcessingResponse(
            BoxProcessingStatus.Success,
            Message: "ok",
            Records: records,
            Weight: null,
            ShouldPrintDropSheet: true,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: true
        );

        const string template = "<html><head></head><body>{% for record in Records %}<div>{{RowNumber}} {{Artnr}}</div>{% endfor %}<footer class=\"page-footer\">{{CurrentPage}}/{{TotalPages}}</footer></body></html>";

        var html = StaTestRunner.Run(() => StuffingSheetHtmlTemplateRenderer.Render(template, response, tenam: "4340558"));

        Assert.Equal(2, Regex.Matches(html, "<section class=\"lfs-sheet-page\">", RegexOptions.CultureInvariant).Count);
        Assert.Contains("<footer class=\"page-footer\">1/2</footer>", html, StringComparison.Ordinal);
        Assert.Contains("<footer class=\"page-footer\">2/2</footer>", html, StringComparison.Ordinal);
        Assert.Contains("<div>41 ART-041</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_HtmlEncodesHeaderFields()
    {
        var response = new BoxProcessingResponse(
            BoxProcessingStatus.Success,
            Message: "ok",
            Records: new[]
            {
                new LabelFlowStudio.Core.Models.LabelRecord
                {
                    Tenam = "4340558",
                    Lndnam = "RU",
                    Gpbez = "A&B <C>"
                }
            },
            Weight: null,
            ShouldPrintDropSheet: true,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: true
        );

        var template = "place={{Gpbez}}";

        var html = StaTestRunner.Run(() => StuffingSheetHtmlTemplateRenderer.Render(template, response, tenam: "4340558"));

        Assert.Contains("place=A&amp;B &lt;C&gt;", html, StringComparison.Ordinal);
    }
}
