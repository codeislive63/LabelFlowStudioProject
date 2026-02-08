using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Printing;
using System.IO;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace LabelFlowStudio.Desktop.Templates;

public static class StuffingSheetHtmlTemplateRenderer
{
    private static readonly Regex ProductsLoopRegex = new(
        @"\{%\s*for\s+product\s+in\s+products\s*%\}(?<body>.*?)\{%\s*endfor\s*%\}",
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

        var products = response.Records
            .OrderBy(record => record.Artbez, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var sum = products.Sum(record => record.Bstmg ?? 0m);
        var sumText = sum.ToString("0.###", CultureInfo.CurrentCulture);

        var barcodeDataUrl = CreateBarcodeDataUrl(tenam);

        var result = template;

        result = ReplaceEncodedToken(result, "country", header?.Lndnam);
        result = ReplaceEncodedToken(result, "te", tenam);

        result = ReplaceEncodedToken(result, "index", header?.Gpplz);
        result = ReplaceEncodedToken(result, "place", header?.Gpbez);
        result = ReplaceEncodedToken(result, "city", header?.Gport1);
        result = ReplaceEncodedToken(result, "street", header?.Gpstrasse);

        result = ReplaceEncodedToken(result, "current_date", currentDate);
        result = ReplaceEncodedToken(result, "current_time", currentTime);

        result = ReplaceEncodedToken(result, "aufid", header?.Aufid);
        result = ReplaceEncodedToken(result, "sum", sumText);

        result = ReplaceRawToken(result, "barcode", barcodeDataUrl);

        result = RenderProductsLoop(result, products);

        return result;
    }

    private static string RenderProductsLoop(string html, IReadOnlyList<LabelRecord> products)
    {
        var match = ProductsLoopRegex.Match(html);

        if (!match.Success)
        {
            return html;
        }

        var rowTemplate = match.Groups["body"].Value;

        var builder = new StringBuilder();

        for (var index = 0; index < products.Count; index++)
        {
            var record = products[index];

            var row = rowTemplate;

            row = ReplaceEncodedToken(row, "product.ROWNUM", (index + 1).ToString(CultureInfo.InvariantCulture));
            row = ReplaceEncodedToken(row, "product.ARTNR", record.Artnr);
            row = ReplaceEncodedToken(row, "product.ARTBEZ", record.Artbez);
            row = ReplaceEncodedToken(row, "product.BSTMG", FormatQuantity(record.Bstmg));

            builder.Append(row);
        }

        var before = html[..match.Index];
        var after = html[(match.Index + match.Length)..];

        return before + builder + after;
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
