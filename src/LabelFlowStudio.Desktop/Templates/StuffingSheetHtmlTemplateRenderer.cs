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
    private const int FirstPageRows = 40;
    private const int NextPageRows = 40;

    private static readonly Regex LoopRegex = new(
        @"\{%\s*for\s+(?<var>\w+)\s+in\s+(?<list>\w+)\s*%\}(?<body>.*?)\{%\s*endfor\s*%\}",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    );

    private static readonly Regex IfRegex = new(
        @"\{%\s*if\s+(?<name>\w+)\s*%\}(?<body>.*?)\{%\s*endif\s*%\}",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    );

    public static string Render(string template, BoxProcessingResponse response, string tenam)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (response == null)
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

        var sumBstText = FormatQuantity(header?.SumBst);

        if (string.IsNullOrWhiteSpace(sumBstText))
        {
            decimal sum = records.Sum(record => record.Bstmg ?? 0m);
            sumBstText = sum.ToString("0.###", CultureInfo.CurrentCulture);
        }

        var barcodeDataUrl = CreateBarcodeDataUrl(tenam);

        var result = template;

        result = ReplaceEncodedToken(result, "Lndnam", header != null ? header.Lndnam : null);
        result = ReplaceEncodedToken(result, "Tenam", tenam);
        result = ReplaceEncodedToken(result, "Gpplz", header != null ? header.Gpplz : null);
        result = ReplaceEncodedToken(result, "Gpbez", header != null ? header.Gpbez : null);
        result = ReplaceEncodedToken(result, "Gport1", header != null ? header.Gport1 : null);
        result = ReplaceEncodedToken(result, "Gpstrasse", header != null ? header.Gpstrasse : null);
        result = ReplaceEncodedToken(result, "Aufid", header != null ? header.Aufid : null);
        result = ReplaceEncodedToken(result, "Market", FormatMarket(header != null ? header.Market : null));

        result = ReplaceEncodedToken(result, "CurrentDate", currentDate);
        result = ReplaceEncodedToken(result, "CurrentTime", currentTime);
        result = ReplaceEncodedToken(result, "SumBst", sumBstText);

        result = ReplaceRawToken(result, "BarcodeDataUri", barcodeDataUrl);

        result = ReplaceEncodedToken(result, "country", header != null ? header.Lndnam : null);
        result = ReplaceEncodedToken(result, "te", tenam);
        result = ReplaceEncodedToken(result, "index", header != null ? header.Gpplz : null);
        result = ReplaceEncodedToken(result, "market", FormatMarket(header != null ? header.Market : null));
        result = ReplaceEncodedToken(result, "place", header != null ? header.Gpbez : null);
        result = ReplaceEncodedToken(result, "city", header != null ? header.Gport1 : null);
        result = ReplaceEncodedToken(result, "street", header != null ? header.Gpstrasse : null);
        result = ReplaceEncodedToken(result, "aufid", header != null ? header.Aufid : null);

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

        var pages = SplitRecordsIntoPages(records);

        if (pages.Count == 0)
        {
            pages.Add(new List<LabelRecord>());
        }

        var bodyRegex = new Regex(
            @"<body[^>]*>(?<body>.*)</body>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var bodyMatch = bodyRegex.Match(templateHtml);
        if (!bodyMatch.Success)
        {
            return templateHtml;
        }

        var bodyStart = bodyMatch.Index;
        var bodyEnd = bodyMatch.Index + bodyMatch.Length;

        var docStart = templateHtml[..bodyStart];
        var docEnd = templateHtml[bodyEnd..];
        var bodyTemplate = bodyMatch.Groups["body"].Value;

        var pageBuilder = new StringBuilder();

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var isFirstPage = pageIndex == 0;
            var isLastPage = pageIndex == pages.Count - 1;

            var pageHtml = bodyTemplate;

            pageHtml = ReplacePageTokens(pageHtml, pageIndex + 1, pages.Count);
            pageHtml = ReplaceBoolSection(pageHtml, "ShowHeader", isFirstPage);
            pageHtml = ReplaceBoolSection(pageHtml, "ShowTotals", isLastPage);
            pageHtml = RenderLoop(pageHtml, pages[pageIndex], GetRowOffsetForPage(pageIndex));

            pageBuilder.Append("<section class=\"lfs-sheet-page\">");
            pageBuilder.Append(pageHtml);
            pageBuilder.Append("</section>");
        }

        var paginationStyle = @"
<style>
    @media print {
        body {
            margin: 0;
        }

        .lfs-sheet-page {
            min-height: 267mm;
            box-sizing: border-box;
            page-break-after: always;
            break-after: page;
        }

        .lfs-sheet-page:last-child {
            page-break-after: auto;
            break-after: auto;
        }

        .page-header {
            display: block !important;
        }

        .page-footer {
            display: none !important;
        }
    }

    @media screen {
        .lfs-sheet-page {
            min-height: 267mm;
            box-sizing: border-box;
        }

        .page-header {
            display: block;
        }

        .page-footer {
            display: none;
        }
    }
</style>";

        if (docStart.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            docStart = Regex.Replace(
                docStart,
                "</head>",
                paginationStyle + "</head>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return string.Concat(docStart, "<body>", pageBuilder.ToString(), "</body>", docEnd);
    }

    private static List<List<LabelRecord>> SplitRecordsIntoPages(IReadOnlyList<LabelRecord> records)
    {
        var result = new List<List<LabelRecord>>();

        if (records.Count == 0)
        {
            result.Add(new List<LabelRecord>());
            return result;
        }

        var index = 0;

        var firstPageCount = Math.Min(FirstPageRows, records.Count);
        result.Add(records.Skip(index).Take(firstPageCount).ToList());
        index += firstPageCount;

        while (index < records.Count)
        {
            var take = Math.Min(NextPageRows, records.Count - index);
            result.Add(records.Skip(index).Take(take).ToList());
            index += take;
        }

        return result;
    }

    private static int GetRowOffsetForPage(int pageIndex)
    {
        if (pageIndex <= 0)
        {
            return 0;
        }

        return FirstPageRows + ((pageIndex - 1) * NextPageRows);
    }

    private static string RenderLoop(string html, IReadOnlyList<LabelRecord> records, int rowOffset)
    {
        var match = LoopRegex.Match(html);

        if (!match.Success)
        {
            return html;
        }

        var listName = match.Groups["list"].Value;

        if (!string.Equals(listName, "Records", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(listName, "products", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var varName = match.Groups["var"].Value;
        var rowTemplate = match.Groups["body"].Value;

        var builder = new StringBuilder();

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var row = rowTemplate;

            row = ReplaceEncodedToken(row, "RowNumber", (rowOffset + index + 1).ToString(CultureInfo.InvariantCulture));
            row = ReplaceEncodedToken(row, "Artnr", record.Artnr);
            row = ReplaceEncodedToken(row, "Artbez", record.Artbez);
            row = ReplaceEncodedToken(row, "Bstmg", FormatQuantity(record.Bstmg));

            row = ReplaceEncodedToken(row, varName + ".ROWNUM", (rowOffset + index + 1).ToString(CultureInfo.InvariantCulture));
            row = ReplaceEncodedToken(row, varName + ".ARTNR", record.Artnr);
            row = ReplaceEncodedToken(row, varName + ".ARTBEZ", record.Artbez);
            row = ReplaceEncodedToken(row, varName + ".BSTMG", FormatQuantity(record.Bstmg));

            builder.Append(row);
        }

        var before = html[..match.Index];
        var after = html[(match.Index + match.Length)..];

        return before + builder + after;
    }

    private static string ReplaceBoolSection(string html, string sectionName, bool show)
    {
        return IfRegex.Replace(html, match =>
        {
            var name = match.Groups["name"].Value;
            var body = match.Groups["body"].Value;

            if (!string.Equals(name, sectionName, StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            return show ? body : string.Empty;
        });
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

    private static string FormatMarket(string market)
    {
        if (string.IsNullOrWhiteSpace(market))
        {
            return string.Empty;
        }

        return market.Trim();
    }

    private static string FormatQuantity(decimal? value)
    {
        if (!value.HasValue)
        {
            return string.Empty;
        }

        return value.Value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private static string ReplaceEncodedToken(string html, string tokenName, string value)
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
        return "data:image/png;base64," + base64;
    }
}