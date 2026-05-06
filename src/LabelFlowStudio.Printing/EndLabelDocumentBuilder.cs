using LabelFlowStudio.Application.BoxProcessing.Contracts;
using Microsoft.Extensions.Options;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LabelFlowStudio.Printing;

/// <summary>
/// Создает WPF-документ торцевой этикетки для печати
/// </summary>
public sealed class EndLabelDocumentBuilder
{
    private readonly IOptionsMonitor<PrintingOptions> _optionsMonitor;

    public EndLabelDocumentBuilder(IOptionsMonitor<PrintingOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    /// <summary>
    /// Создает документ торцевой этикетки
    /// </summary>
    public FixedDocument Build(BoxProcessingResponse response, string tenam)
    {
        ArgumentNullException.ThrowIfNull(response);

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
        const int barcodeHeight = 60;

        var barcodeImage = BarcodeImageFactory.CreateCode128(tenam, barcodeWidth, barcodeHeight);

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

    // Переводит миллиметры в WPF DIP
    private static double MillimetersToDip(double millimeters)
    {
        return (millimeters / 25.4) * 96.0;
    }
}
