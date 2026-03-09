using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Printing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace LabelFlowStudio.Desktop.Templates;

public static class StuffingSheetHtmlTemplateRenderer
{
    // Conservative row limit so totals/footer always stay on the same physical page.
    private const int DefaultRowsPerPage = 32;

    // Supports both:
    //   {% for product in products %}...{% endfor %} (legacy)
    //   {% for record in Records %}...{% endfor %} (unified)
    private static readonly Regex LoopRegex = new(
        @"\{%\s*for\s+(?<var>\w+)\s+in\s+(?<list>\w+)\s*%\}(?<body>.*?)\{%\s*endfor\s*%\}",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    );

    public static string Render(string template, BoxProcessingResponse response, string tenam)
    {
        if (template is null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        var header = response.Records.Count > 0 ? response.Records[0] : null;

        var now = DateTime.Now;
        var currentDate = now.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
        var currentTime = now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

        var records = response.Records
            .OrderBy(record => record.Artbez, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Итого штук: предпочитаем поле из view (SumBst), иначе суммируем BSTMG
        var sumBstText = FormatQuantity(header?.SumBst);

        if (string.IsNullOrWhiteSpace(sumBstText))
        {
            decimal sum = records.Sum(record => record.Bstmg ?? 0m);
            sumBstText = sum.ToString("0.###", CultureInfo.CurrentCulture);
        }

        var barcodeDataUrl = CreateBarcodeDataUrl(tenam);

        var result = template;

        // Unified tokens (field names from LabelRecord / app context)
        result = ReplaceEncodedToken(result, "Lndnam", header?.Lndnam);
        result = ReplaceEncodedToken(result, "Tenam", tenam);
        result = ReplaceEncodedToken(result, "Gpplz", header?.Gpplz);
        result = ReplaceEncodedToken(result, "Gpbez", header?.Gpbez);
        result = ReplaceEncodedToken(result, "Gport1", header?.Gport1);
        result = ReplaceEncodedToken(result, "Gpstrasse", header?.Gpstrasse);
        result = ReplaceEncodedToken(result, "Aufid", header?.Aufid);
        result = ReplaceEncodedToken(result, "Market", FormatMarket(header?.Market));

        result = ReplaceEncodedToken(result, "CurrentDate", currentDate);
        result = ReplaceEncodedToken(result, "CurrentTime", currentTime);

        result = ReplaceEncodedToken(result, "SumBst", sumBstText);

        // Barcode must be raw (not HTML-encoded)
        result = ReplaceRawToken(result, "BarcodeDataUri", barcodeDataUrl);

        // Backward-compatible aliases (python/Jinja names)
        result = ReplaceEncodedToken(result, "country", header?.Lndnam);
        result = ReplaceEncodedToken(result, "te", tenam);
        result = ReplaceEncodedToken(result, "index", header?.Gpplz);
        result = ReplaceEncodedToken(result, "market", FormatMarket(header?.Market));
        result = ReplaceEncodedToken(result, "place", header?.Gpbez);
        result = ReplaceEncodedToken(result, "city", header?.Gport1);
        result = ReplaceEncodedToken(result, "street", header?.Gpstrasse);
        result = ReplaceEncodedToken(result, "aufid", header?.Aufid);

        result = ReplaceEncodedToken(result, "current_date", currentDate);
        result = ReplaceEncodedToken(result, "current_time", currentTime);

        result = ReplaceEncodedToken(result, "sum", sumBstText);
        result = ReplaceRawToken(result, "barcode", barcodeDataUrl);

        result = RenderPaginatedDocument(result, records);

        return result;
    }

    private static string RenderPaginatedDocument(string templateHtml, IReadOnlyList<LabelRecord> records)
    {
        var loopMatch = LoopRegex.Match(templateHtml);
        if (!loopMatch.Success)
        {
            return templateHtml;
        }

        var pages = records
            .Select((record, index) => new { record, index })
            .GroupBy(item => item.index / DefaultRowsPerPage)
            .Select(group => group.Select(item => item.record).ToList())
            .ToList();

        if (pages.Count == 0)
        {
            pages.Add(new List<LabelRecord>());
        }

        if (pages.Count == 1)
        {
            var singlePage = ReplacePageTokens(templateHtml, currentPage: 1, totalPages: 1);
            return RenderLoop(singlePage, pages[0], rowOffset: 0);
        }

        var bodyRegex = new Regex(@"<body[^>]*>(?<body>.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var bodyMatch = bodyRegex.Match(templateHtml);
        if (!bodyMatch.Success)
        {
            var singlePage = ReplacePageTokens(templateHtml, currentPage: 1, totalPages: 1);
            return RenderLoop(singlePage, pages[0], rowOffset: 0);
        }

        var bodyStart = bodyMatch.Index;
        var bodyEnd = bodyMatch.Index + bodyMatch.Length;

        var docStart = templateHtml[..bodyStart];
        var docEnd = templateHtml[bodyEnd..];
        var bodyTemplate = bodyMatch.Groups["body"].Value;

        var pageBuilder = new StringBuilder();
        for (var index = 0; index < pages.Count; index++)
        {
            var pageHtml = ReplacePageTokens(bodyTemplate, currentPage: index + 1, totalPages: pages.Count);
            pageHtml = RenderLoop(pageHtml, pages[index], rowOffset: index * DefaultRowsPerPage);

            pageBuilder.Append("<section class=\"lfs-sheet-page\">");
            pageBuilder.Append(pageHtml);
            pageBuilder.Append("</section>");
        }

        var paginationStyle = "<style>@media print {.lfs-sheet-page{page-break-after:always;break-after:page;break-inside:avoid;display:flex;flex-direction:column;min-height:267mm;box-sizing:border-box;}.lfs-sheet-page:last-child{page-break-after:auto;break-after:auto;}.lfs-sheet-page .page-footer{position:static!important;margin-top:auto!important;padding-top:6mm;}}@media screen {.lfs-sheet-page{display:flex;flex-direction:column;min-height:267mm;box-sizing:border-box;}.lfs-sheet-page .page-footer{margin-top:auto;padding-top:6mm;}}</style>";
        if (docStart.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            docStart = Regex.Replace(docStart, "</head>", paginationStyle + "</head>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return string.Concat(docStart, "<body>", pageBuilder, "</body>", docEnd);
    }

    private static string RenderLoop(string html, IReadOnlyList<LabelRecord> records, int rowOffset)
    {
        var match = LoopRegex.Match(html);

        if (!match.Success)
        {
            return html;
        }

        var listName = match.Groups["list"].Value;

        // Only handle our known list names (new + legacy)
        if (!string.Equals(listName, "Records", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(listName, "products", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var varName = match.Groups["var"].Value; // record/product
        var rowTemplate = match.Groups["body"].Value;

        var builder = new StringBuilder();

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var row = rowTemplate;

            // Unified row tokens
            row = ReplaceEncodedToken(row, "RowNumber", (rowOffset + index + 1).ToString(CultureInfo.InvariantCulture));
            row = ReplaceEncodedToken(row, "Artnr", record.Artnr);
            row = ReplaceEncodedToken(row, "Artbez", record.Artbez);
            row = ReplaceEncodedToken(row, "Bstmg", FormatQuantity(record.Bstmg));

            // Legacy row tokens (keep working)
            row = ReplaceEncodedToken(row, $"{varName}.ROWNUM", (rowOffset + index + 1).ToString(CultureInfo.InvariantCulture));
            row = ReplaceEncodedToken(row, $"{varName}.ARTNR", record.Artnr);
            row = ReplaceEncodedToken(row, $"{varName}.ARTBEZ", record.Artbez);
            row = ReplaceEncodedToken(row, $"{varName}.BSTMG", FormatQuantity(record.Bstmg));

            builder.Append(row);
        }

        var before = html[..match.Index];
        var after = html[(match.Index + match.Length)..];

        return before + builder + after;
    }

    private static string ReplacePageTokens(string html, int currentPage, int totalPages)
    {
        var current = currentPage.ToString(CultureInfo.InvariantCulture);
        var total = totalPages.ToString(CultureInfo.InvariantCulture);

        html = ReplaceEncodedToken(html, "CurrentPage", current);
        html = ReplaceEncodedToken(html, "TotalPages", total);
        html = ReplaceEncodedToken(html, "PageNumber", current);
        html = ReplaceEncodedToken(html, "PageCount", total);

        return html;
    }

    private static string FormatMarket(string? market)
    {
        if (string.IsNullOrWhiteSpace(market))
        {
            return string.Empty;
        }

        return $"{market.Trim()}";
    }

    private static string FormatQuantity(decimal? value)
    {
        if (!value.HasValue)
        {
            return string.Empty;
        }

        return value.Value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private static string ReplaceEncodedToken(string html, string tokenName, string? value)
    {
        var safeValue = WebUtility.HtmlEncode(value ?? string.Empty);
        return ReplaceTokenInternal(html, tokenName, safeValue);
    }

    private static string ReplaceRawToken(string html, string tokenName, string value)
    {
        return ReplaceTokenInternal(html, tokenName, value ?? string.Empty);
    }

    private static string ReplaceTokenInternal(string html, string tokenName, string replacement)
    {
        var escaped = Regex.Escape(tokenName);

        // {{ token }} with any whitespace/newlines
        var regex = new Regex(@"\{\{\s*" + escaped + @"\s*\}\}", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return regex.Replace(html, replacement);
    }

    private static string CreateBarcodeDataUrl(string tenam)
    {
        var barcodeBitmap = BarcodeImageFactory.CreateCode128(tenam, width: 900, height: 180);

        using var stream = new MemoryStream();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(barcodeBitmap));
        encoder.Save(stream);

        var base64 = Convert.ToBase64String(stream.ToArray());
        return $"data:image/png;base64,{base64}";
    }
}
