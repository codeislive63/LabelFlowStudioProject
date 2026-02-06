using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Printing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Windows.Media.Imaging;

namespace LabelFlowStudio.Desktop.Templates;

public static class EndLabelHtmlTemplateRenderer
{
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

        var barcodeDataUrl = CreateBarcodeDataUrl(tenam);

        // Вес: берем из response.Weight (если есть), иначе из первой записи (если у тебя там есть Brutto)
        var brutto = response.Weight;
        var bruttoText = brutto.HasValue
            ? brutto.Value.ToString("0.###", CultureInfo.CurrentCulture)
            : string.Empty;

        var countBstText = header?.CountBst.HasValue == true
            ? header.CountBst.Value.ToString("0.###", CultureInfo.CurrentCulture)
            : string.Empty;

        var sumBstText = header?.SumBst.HasValue == true
            ? header.SumBst.Value.ToString("0.###", CultureInfo.CurrentCulture)
            : string.Empty;

        var result = template;

        // Tenam
        result = ReplaceEncoded(result, "{{Tenam}}", tenam);
        result = ReplaceEncoded(result, "{{TENAM}}", tenam);

        // Barcode
        result = result.Replace("{{BarcodeDataUri}}", barcodeDataUrl, StringComparison.Ordinal);
        result = result.Replace("{{BARCODE_DATA_URL}}", barcodeDataUrl, StringComparison.Ordinal);

        // Поля из header (LabelRecord)
        result = ReplaceEncoded(result, "{{Lfakdnr}}", header?.Lfakdnr);
        result = ReplaceEncoded(result, "{{Gpbez}}", header?.Gpbez);
        result = ReplaceEncoded(result, "{{Gport1}}", header?.Gport1);
        result = ReplaceEncoded(result, "{{Gpstrasse}}", header?.Gpstrasse);
        result = ReplaceEncoded(result, "{{Bstchgnam5}}", header?.Bstchgnam5);

        // Вес и KPI
        result = ReplaceEncoded(result, "{{Brutto}}", bruttoText);

        // В шаблоне у тебя {{Countbst}} (b маленькая) — подстрахуемся обоими вариантами
        result = ReplaceEncoded(result, "{{Countbst}}", countBstText);
        result = ReplaceEncoded(result, "{{CountBst}}", countBstText);

        result = ReplaceEncoded(result, "{{SumBst}}", sumBstText);

        return result;
    }

    private static string ReplaceEncoded(string html, string token, string? value)
    {
        var safeValue = WebUtility.HtmlEncode(value ?? string.Empty);
        return html.Replace(token, safeValue, StringComparison.Ordinal);
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
