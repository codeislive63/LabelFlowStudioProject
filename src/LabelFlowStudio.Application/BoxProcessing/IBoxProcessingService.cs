namespace LabelFlowStudio.Application.BoxProcessing;

public interface IBoxProcessingService
{
    Task<BoxProcessingResponse> ProcessAsync(BoxProcessingRequest request, CancellationToken cancellationToken);
}
