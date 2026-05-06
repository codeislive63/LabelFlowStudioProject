using LabelFlowStudio.Application.BoxProcessing.Contracts;

namespace LabelFlowStudio.Printing;

public interface IPrintService
{
    Task PrintDropSheetAsync(BoxProcessingResponse response, string tenam, CancellationToken cancellationToken);

    Task PrintEmptyDropSheetAsync(string tenam, CancellationToken cancellationToken);

    Task PrintEndLabelAsync(BoxProcessingResponse response, string tenam, CancellationToken cancellationToken);
}
