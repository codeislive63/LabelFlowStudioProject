using LabelFlowStudio.Application.BoxProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Printing;
using System.Windows.Controls;
using System.Windows.Documents;

namespace LabelFlowStudio.Printing;

/// <summary>
/// Сервис печати документов через WPF PrintDialog
/// </summary>
public sealed class WpfPrintService : IPrintService
{
    private readonly IOptionsMonitor<PrintingOptions> _optionsMonitor;
    private readonly EndLabelDocumentBuilder _endLabelDocumentBuilder;
    private readonly ILogger<WpfPrintService> _logger;

    private readonly SemaphoreSlim _printGate = new(1, 1);

    public WpfPrintService(
        IOptionsMonitor<PrintingOptions> optionsMonitor,
        EndLabelDocumentBuilder endLabelDocumentBuilder,
        ILogger<WpfPrintService> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _endLabelDocumentBuilder = endLabelDocumentBuilder ?? throw new ArgumentNullException(nameof(endLabelDocumentBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Печатает лист сброса
    /// </summary>
    public async Task PrintDropSheetAsync(BoxProcessingResponse response, string tenam, CancellationToken cancellationToken)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        var document = DropSheetDocumentBuilder.Build(response, tenam);
        var options = _optionsMonitor.CurrentValue;

        await PrintDocumentAsync(
            document,
            jobName: $"DropSheet {tenam}",
            printerName: options.DropSheetPrinterName,
            showDialog: options.ShowDialogForDropSheet,
            cancellationToken);
    }

    /// <summary>
    /// Печатает пустой лист сброса
    /// </summary>
    public async Task PrintEmptyDropSheetAsync(string tenam, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Tenam is required", nameof(tenam));
        }

        var document = DropSheetDocumentBuilder.BuildEmpty(tenam);
        var options = _optionsMonitor.CurrentValue;

        await PrintDocumentAsync(
            document,
            jobName: $"EmptyDropSheet {tenam}",
            printerName: options.DropSheetPrinterName,
            showDialog: options.ShowDialogForDropSheet,
            cancellationToken);
    }

    /// <summary>
    /// Печатает торцевую этикетку
    /// </summary>
    public async Task PrintEndLabelAsync(BoxProcessingResponse response, string tenam, CancellationToken cancellationToken)
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
        var document = _endLabelDocumentBuilder.Build(response, tenam);

        await PrintDocumentAsync(
            document,
            jobName: $"EndLabel {tenam}",
            printerName: options.EndLabelPrinterName,
            showDialog: options.ShowDialogForEndLabel,
            cancellationToken
        );
    }

    // Выполняет печать документа в UI потоке
    private async Task PrintDocumentAsync(
        IDocumentPaginatorSource document,
        string jobName,
        string? printerName,
        bool showDialog,
        CancellationToken cancellationToken)
    {
        await _printGate.WaitAsync(cancellationToken);

        try
        {
            await RunOnUiThreadAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var printDialog = new PrintDialog();

                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    var printQueue = TryGetPrintQueue(printerName);

                    if (printQueue is null)
                    {
                        throw new InvalidOperationException($"Принтер не найден: {printerName}");
                    }

                    printDialog.PrintQueue = printQueue;
                }

                if (showDialog)
                {
                    var accepted = printDialog.ShowDialog();

                    if (accepted != true)
                    {
                        throw new OperationCanceledException("Печать отменена");
                    }
                }

                printDialog.PrintDocument(document.DocumentPaginator, jobName);
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Print failed: {JobName}", jobName);
            throw;
        }
        finally
        {
            _printGate.Release();
        }
    }

    // Возвращает очередь печати по имени принтера
    private static PrintQueue? TryGetPrintQueue(string printerName)
    {
        try
        {
            using var server = new LocalPrintServer();
            return server.GetPrintQueue(printerName);
        }
        catch
        {
            return null;
        }
    }

    // Выполняет действие в UI потоке с учетом отмены
    private static Task RunOnUiThreadAsync(Action action, CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, System.Windows.Threading.DispatcherPriority.Send, cancellationToken).Task;
    }
}
