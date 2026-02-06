using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LabelFlowStudio.Printing;

public static class DropSheetDocumentBuilder
{
    public static FlowDocument Build(BoxProcessingResponse response, string tenam)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        var document = CreateBaseDocument();

        document.Blocks.Add(new Paragraph(new Run($"Лист сброса  TENAM {tenam}"))
        {
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var barcodeWidth = 420;
        var barcodeHeight = 80;

        var barcodeImage = BarcodeImageFactory.CreateCode128(tenam, barcodeWidth, barcodeHeight);

        document.Blocks.Add(new BlockUIContainer(new Image
        {
            Source = barcodeImage,
            Height = barcodeHeight,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 0, 10)
        }));

        if (response.Records.Count > 0)
        {
            var header = response.Records[0];

            document.Blocks.Add(new Paragraph(new Run($"AUFID {header.Aufid}"))
            {
                Margin = new Thickness(0, 0, 0, 2)
            });

            document.Blocks.Add(new Paragraph(new Run($"{header.Gpbez}  {header.Gport1}  {header.Gpstrasse}"))
            {
                Margin = new Thickness(0, 0, 0, 10)
            });
        }

        document.Blocks.Add(BuildTable(response.Records));
        document.Blocks.Add(new Paragraph(new Run($"Позиций {response.Records.Count}"))
        {
            Margin = new Thickness(0, 10, 0, 0),
            FontWeight = FontWeights.SemiBold
        });

        return document;
    }

    public static FlowDocument BuildEmpty(string tenam)
    {
        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        var document = CreateBaseDocument();

        document.Blocks.Add(new Paragraph(new Run($"Пустой лист сброса  TENAM {tenam}"))
        {
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var barcodeWidth = 420;
        var barcodeHeight = 80;

        var barcodeImage = BarcodeImageFactory.CreateCode128(tenam, barcodeWidth, barcodeHeight);

        document.Blocks.Add(new BlockUIContainer(new Image
        {
            Source = barcodeImage,
            Height = barcodeHeight,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 0, 12)
        }));

        document.Blocks.Add(new Paragraph(new Run("Вес отсутствует"))
        {
            FontSize = 14
        });

        return document;
    }

    private static FlowDocument CreateBaseDocument()
    {
        return new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            PagePadding = new Thickness(40),
            ColumnWidth = double.PositiveInfinity
        };
    }

    private static Block BuildTable(IReadOnlyList<LabelRecord> records)
    {
        var table = new Table
        {
            CellSpacing = 0
        };

        table.Columns.Add(new TableColumn { Width = new GridLength(200) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(80) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var headerRow = new TableRow();
        group.Rows.Add(headerRow);

        AddHeaderCell(headerRow, "ARTNR");
        AddHeaderCell(headerRow, "ARTBEZ");
        AddHeaderCell(headerRow, "BSTMG");

        foreach (var record in records)
        {
            var row = new TableRow();
            group.Rows.Add(row);

            AddBodyCell(row, record.Artnr);
            AddBodyCell(row, record.Artbez);
            AddBodyCell(row, record.Bstmg?.ToString() ?? string.Empty);
        }

        return table;
    }

    private static void AddHeaderCell(TableRow row, string text)
    {
        row.Cells.Add(new TableCell(new Paragraph(new Run(text)))
        {
            FontWeight = FontWeights.SemiBold,
            Background = Brushes.LightGray,
            Padding = new Thickness(6),
            BorderThickness = new Thickness(0.5),
            BorderBrush = Brushes.Gray
        });
    }

    private static void AddBodyCell(TableRow row, string text)
    {
        row.Cells.Add(new TableCell(new Paragraph(new Run(text)))
        {
            Padding = new Thickness(6),
            BorderThickness = new Thickness(0.5),
            BorderBrush = Brushes.Gray
        });
    }
}
