namespace LabelFlowStudio.Application.BoxProcessing.Contracts;

/// <summary>
/// Итоговый статус обработки короба
/// </summary>
public enum BoxProcessingStatus
{
    Success = 0,
    NotFound = 1,
    NeedWeight = 2,
    Error = 3
}
