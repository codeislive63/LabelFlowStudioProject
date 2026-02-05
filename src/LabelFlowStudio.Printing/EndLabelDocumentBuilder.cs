using LabelFlowStudio.Application.BoxProcessing;
using Microsoft.Extensions.Options;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;

namespace LabelFlowStudio.Printing;

public sealed class EndLabelDocumentBuilder
{
    private readonly IOptionsMonitor<PrintingOptions> _optionsMonitor;

    public EndLabelDocumentBuilder(IOptionsMonitor<PrintingOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    public FixedDocument Build(BoxProcessingResponse response, string tenam)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        var options = _optionsMonitor.CurrentValue;

        var pageWidth = MillimetersToDip(options.EndLabelWidthMm);
        var pageHeight = MillimetersToDip(options.EndLabelHeightMm);

        var fixedDocument = new FixedDocument();

        var page = new FixedPage
        {
            Width = pageWidth,
            Height = pageHeight
        };

        var margin = new Thickness(12);

        var stack = new System.Windows.Controls.StackPanel
        {
            Margin = margin
        };

        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = $"TENAM {tenam}",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var barcodeWidth = (int)Math.Max(1, Math.Round(pageWidth - margin.Left - margin.Right));
        var barcodeHeight = 60;

        var barcodeImage = CreateBarcode(tenam, barcodeWidth, barcodeHeight);

        stack.Children.Add(new System.Windows.Controls.Image
        {
            Source = barcodeImage,
            Stretch = Stretch.Uniform,
            Height = barcodeHeight,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var weightText = response.Weight.HasValue
            ? response.Weight.Value.ToString("0.###")
            : string.Empty;

        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = string.IsNullOrWhiteSpace(weightText) ? "Вес отсутствует" : $"Вес {weightText}",
            FontSize = 16
        });

        page.Children.Add(stack);

        var pageContent = new PageContent
        {
            Child = page
        };

        fixedDocument.Pages.Add(pageContent);

        return fixedDocument;
    }

    private static double MillimetersToDip(double millimeters)
    {
        return (millimeters / 25.4) * 96.0;
    }

    private static BitmapSource CreateBarcode(string value, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Barcode value is required", nameof(value));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new ZXing.Common.EncodingOptions
            {
                Width = width,
                Height = height,
                Margin = 0,
                PureBarcode = true
            }
        };

        var pixelData = writer.Write(value);
        var stride = pixelData.Width * 4;

        var bitmap = BitmapSource.Create(
            pixelData.Width,
            pixelData.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixelData.Pixels,
            stride
        );

        bitmap.Freeze();
        return bitmap;
    }
}
