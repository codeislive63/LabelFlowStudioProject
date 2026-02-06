using LabelFlowStudio.Printing;
using System.IO;
using System.Net;
using System.Windows.Media.Imaging;

namespace LabelFlowStudio.Desktop.Templates;

public static class EndLabelHtmlTemplateRenderer
{
    public static string Render(string template, string tenam, decimal? weight)
    {
        if (template is null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        var barcodeDataUrl = CreateBarcodeDataUrl(tenam);

        var weightText = weight.HasValue
            ? $"Вес {weight.Value:0.###}"
            : "Вес отсутствует";

        var encodedTenam = WebUtility.HtmlEncode(tenam);
        var encodedWeightText = WebUtility.HtmlEncode(weightText);
        var bruttoValue = weight.HasValue ? weight.Value.ToString("0.###") : string.Empty;

        return template
            // TENAM
            .Replace("{{TENAM}}", encodedTenam, StringComparison.Ordinal)
            .Replace("{{Tenam}}", encodedTenam, StringComparison.Ordinal)

            // weight text + numeric
            .Replace("{{WEIGHT_TEXT}}", encodedWeightText, StringComparison.Ordinal)
            .Replace("{{Brutto}}", WebUtility.HtmlEncode(bruttoValue), StringComparison.Ordinal)

            // barcode
            .Replace("{{BARCODE_DATA_URL}}", barcodeDataUrl, StringComparison.Ordinal)
            .Replace("{{BARCODE_DATA_URI}}", barcodeDataUrl, StringComparison.Ordinal)
            .Replace("{{BarcodeDataUrl}}", barcodeDataUrl, StringComparison.Ordinal)
            .Replace("{{BarcodeDataUri}}", barcodeDataUrl, StringComparison.Ordinal);
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
