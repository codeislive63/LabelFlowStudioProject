namespace LabelFlowStudio.App.BoxProcessing;

public interface IBoxProcessingService
{
    Task<BoxProcessingResponse> ProcessAsync(BoxProcessingRequest request, CancellationToken cancellationToken);
}
