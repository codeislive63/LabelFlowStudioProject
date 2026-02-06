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

        return template
            .Replace("{{TENAM}}", WebUtility.HtmlEncode(tenam), StringComparison.Ordinal)
            .Replace("{{WEIGHT_TEXT}}", WebUtility.HtmlEncode(weightText), StringComparison.Ordinal)
            .Replace("{{BARCODE_DATA_URL}}", barcodeDataUrl, StringComparison.Ordinal);
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
