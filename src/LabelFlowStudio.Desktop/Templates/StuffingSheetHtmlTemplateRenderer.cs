using LabelFlowStudio.Application.BoxProcessing.Contracts;
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
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex IfRegex = new(
        @"\{%\s*if\s+(?<name>\w+)\s*%\}(?<body>.*?)\{%\s*endif\s*%\}",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static string Render(string template, BoxProcessingResponse response, string tenam)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(response);

        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        var header = response.Records.FirstOrDefault();
        var records = response.Records
            .OrderBy(record => record.Artbez, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var now = DateTime.Now;
        var currentDate = now.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
        var currentTime = now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

        var sumBst = FormatQuantity(header?.SumBst)
            ?? records.Sum(record => record.Bstmg ?? 0m).ToString("0.###", CultureInfo.CurrentCulture);

        var barcodeDataUrl = CreateBarcodeDataUrl(tenam);
        var market = FormatMarket(header?.Market);

        var result = template;

        foreach (var (token, value) in new (string Token, string? Value)[]
        {
            ("Lndnam", header?.Lndnam),
            ("Tenam", tenam),
            ("Gpplz", header?.Gpplz),
            ("Gpbez", header?.Gpbez),
            ("Gport1", header?.Gport1),
            ("Gpstrasse", header?.Gpstrasse),
            ("Aufid", header?.Aufid),
            ("Market", market),
            ("CurrentDate", currentDate),
            ("CurrentTime", currentTime),
            ("SumBst", sumBst),
            ("country", header?.Lndnam),
            ("te", tenam),
            ("index", header?.Gpplz),
            ("market", market),
            ("place", header?.Gpbez),
            ("city", header?.Gport1),
            ("street", header?.Gpstrasse),
            ("aufid", header?.Aufid),
            ("current_date", currentDate),
            ("current_time", currentTime),
            ("sum", sumBst)
        })
        {
            result = ReplaceEncodedToken(result, token, value);
        }

        result = ReplaceRawToken(result, "BarcodeDataUri", barcodeDataUrl);
        result = ReplaceRawToken(result, "barcode", barcodeDataUrl);

        return RenderPaginatedDocument(result, records);
    }

    private static string RenderPaginatedDocument(string templateHtml, IReadOnlyList<LabelRecord> records)
    {
        if (!LoopRegex.IsMatch(templateHtml))
        {
            return templateHtml;
        }

        var pages = SplitRecordsIntoPages(records);
        var bodyMatch = Regex.Match(templateHtml, @"<body[^>]*>(?<body>.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!bodyMatch.Success)
        {
            return RenderLoop(templateHtml, records, rowOffset: 0);
        }

        var docStart = templateHtml[..bodyMatch.Index];
        var docEnd = templateHtml[(bodyMatch.Index + bodyMatch.Length)..];
        var bodyTemplate = bodyMatch.Groups["body"].Value;

        var pageBuilder = new StringBuilder();

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var pageHtml = bodyTemplate;
            var isFirstPage = pageIndex == 0;
            var isLastPage = pageIndex == pages.Count - 1;

            pageHtml = ReplacePageTokens(pageHtml, pageIndex + 1, pages.Count);
            pageHtml = ReplaceBoolSection(pageHtml, "ShowHeader", isFirstPage);
            pageHtml = ReplaceBoolSection(pageHtml, "ShowTotals", isLastPage);
            pageHtml = RenderLoop(pageHtml, pages[pageIndex], GetRowOffsetForPage(pageIndex));

            pageBuilder.Append("<section class=\"lfs-sheet-page\">");
            pageBuilder.Append(pageHtml);
            pageBuilder.Append("</section>");
        }

        docStart = InjectPaginationStyle(docStart);
        return string.Concat(docStart, "<body>", pageBuilder.ToString(), "</body>", docEnd);
    }

    private static List<List<LabelRecord>> SplitRecordsIntoPages(IReadOnlyList<LabelRecord> records)
    {
        if (records.Count == 0)
        {
            return new List<List<LabelRecord>> { new() };
        }

        var pages = new List<List<LabelRecord>>();
        var index = 0;

        var firstPageCount = Math.Min(FirstPageRows, records.Count);
        pages.Add(records.Skip(index).Take(firstPageCount).ToList());
        index += firstPageCount;

        while (index < records.Count)
        {
            var take = Math.Min(NextPageRows, records.Count - index);
            pages.Add(records.Skip(index).Take(take).ToList());
            index += take;
        }

        return pages;
    }

    private static int GetRowOffsetForPage(int pageIndex) => pageIndex <= 0
        ? 0
        : FirstPageRows + ((pageIndex - 1) * NextPageRows);

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
            var rowNumber = (rowOffset + index + 1).ToString(CultureInfo.InvariantCulture);

            var row = rowTemplate;
            foreach (var (token, value) in new (string Token, string? Value)[]
            {
                ("RowNumber", rowNumber),
                ("Artnr", record.Artnr),
                ("Artbez", record.Artbez),
                ("Bstmg", FormatQuantity(record.Bstmg)),
                ($"{varName}.ROWNUM", rowNumber),
                ($"{varName}.ARTNR", record.Artnr),
                ($"{varName}.ARTBEZ", record.Artbez),
                ($"{varName}.BSTMG", FormatQuantity(record.Bstmg))
            })
            {
                row = ReplaceEncodedToken(row, token, value);
            }

            builder.Append(row);
        }

        return string.Concat(html[..match.Index], builder.ToString(), html[(match.Index + match.Length)..]);
    }

    private static string ReplaceBoolSection(string html, string sectionName, bool show) =>
        IfRegex.Replace(html, match =>
        {
            var name = match.Groups["name"].Value;
            if (!string.Equals(name, sectionName, StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            return show ? match.Groups["body"].Value : string.Empty;
        });

    private static string ReplacePageTokens(string html, int currentPage, int totalPages)
    {
        var current = currentPage.ToString(CultureInfo.InvariantCulture);
        var total = totalPages.ToString(CultureInfo.InvariantCulture);

        foreach (var token in new[] { "CurrentPage", "PageNumber" })
        {
            html = ReplaceEncodedToken(html, token, current);
        }

        foreach (var token in new[] { "TotalPages", "PageCount" })
        {
            html = ReplaceEncodedToken(html, token, total);
        }

        return html;
    }

    private static string FormatMarket(string? market) => market?.Trim() ?? string.Empty;

    private static string? FormatQuantity(decimal? value) =>
        value.HasValue
            ? value.Value.ToString("0.###", CultureInfo.CurrentCulture)
            : null;

    private static string ReplaceEncodedToken(string html, string tokenName, string? value) =>
        ReplaceTokenInternal(html, tokenName, WebUtility.HtmlEncode(value ?? string.Empty));

    private static string ReplaceRawToken(string html, string tokenName, string? value) =>
        ReplaceTokenInternal(html, tokenName, value ?? string.Empty);

    private static string ReplaceTokenInternal(string html, string tokenName, string replacement)
    {
        var escaped = Regex.Escape(tokenName);
        var regex = new Regex(@"\{\{\s*" + escaped + @"\s*\}\}", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return regex.Replace(html, replacement);
    }

    private static string InjectPaginationStyle(string documentStart)
    {
        const string paginationStyle = @"
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

        if (!documentStart.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            return documentStart;
        }

        return Regex.Replace(documentStart, "</head>", paginationStyle + "</head>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string CreateBarcodeDataUrl(string tenam)
    {
        var barcodeBitmap = BarcodeImageFactory.CreateCode128(tenam, width: 900, height: 180);

        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(barcodeBitmap));
        encoder.Save(stream);

        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }
}
