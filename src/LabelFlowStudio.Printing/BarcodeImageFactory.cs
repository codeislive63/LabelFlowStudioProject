using System.Windows.Media.Imaging;
using System.Windows.Media;
using ZXing;

namespace LabelFlowStudio.Printing;

public static class BarcodeImageFactory
{
    public static BitmapSource CreateCode128(string value, int width, int height)
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
