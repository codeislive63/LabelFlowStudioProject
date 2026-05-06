using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Policies;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.BoxProcessing;

/// <summary>
/// Обрабатывает отсканированный короб и определяет сценарий дальнейших действий
/// </summary>
public sealed class BoxProcessingService : IBoxProcessingService
{
    private const string EmptyTenamMessage = "TENAM пустой";
    private const string DataNotFoundMessage = "Данных по коробу не найдено";
    private const string DataLoadedMessage = "Данные загружены";

    private readonly ILabelRepository _labelRepository;
    private readonly IBoxWeightResolver _weightResolver;
    private readonly IBoxProcessingPolicy _processingPolicy;

    public BoxProcessingService(
        ILabelRepository labelRepository,
        IBoxWeightResolver weightResolver,
        IBoxProcessingPolicy processingPolicy)
    {
        _labelRepository = labelRepository ?? throw new ArgumentNullException(nameof(labelRepository));
        _weightResolver = weightResolver ?? throw new ArgumentNullException(nameof(weightResolver));
        _processingPolicy = processingPolicy ?? throw new ArgumentNullException(nameof(processingPolicy));
    }

    /// <summary>
    /// Выполняет обработку данных короба и возвращает итоговое действие для печати
    /// </summary>
    public async Task<BoxProcessingResponse> ProcessAsync(
        BoxProcessingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedTenam = NormalizeTenam(request.Tenam);

        if (string.IsNullOrWhiteSpace(normalizedTenam))
        {
            return CreateErrorResponse(EmptyTenamMessage, Array.Empty<LabelRecord>());
        }

        var records = await _labelRepository.GetByTenamAsync(normalizedTenam, cancellationToken);

        if (records.Count == 0)
        {
            return CreateNotFoundResponse(records);
        }

        var weightResolution = _weightResolver.Resolve(records);

        if (weightResolution.HasConflict)
        {
            return CreateErrorResponse(weightResolution.Message, records);
        }

        if (!weightResolution.HasWeight)
        {
            return CreateNeedWeightResponse(records, request, weightResolution.Message);
        }

        return CreateSuccessResponse(records, request, weightResolution.Weight!.Value);
    }

    // Нормализует код короба перед обращением к данным
    private static string NormalizeTenam(string tenam) => (tenam ?? string.Empty).Trim();

    // Формирует ответ ошибки
    private static BoxProcessingResponse CreateErrorResponse(
        string message,
        IReadOnlyList<LabelRecord> records) =>
        new(
            Status: BoxProcessingStatus.Error,
            Message: message,
            Records: records,
            Weight: null,
            PrintPlan: PrintPlan.None
        );

    // Формирует ответ, когда записи по коробу не найдены
    private static BoxProcessingResponse CreateNotFoundResponse(IReadOnlyList<LabelRecord> records) =>
        new(
            Status: BoxProcessingStatus.NotFound,
            Message: DataNotFoundMessage,
            Records: records,
            Weight: null,
            PrintPlan: PrintPlan.None
        );

    // Формирует ответ для случая, когда вес не найден
    private BoxProcessingResponse CreateNeedWeightResponse(
        IReadOnlyList<LabelRecord> records,
        BoxProcessingRequest request,
        string message) =>
        new(
            Status: BoxProcessingStatus.NeedWeight,
            Message: message,
            Records: records,
            Weight: null,
            PrintPlan: _processingPolicy.CreateMissingWeightPrintPlan(request)
        );

    // Формирует успешный ответ, когда данные и вес получены
    private BoxProcessingResponse CreateSuccessResponse(
        IReadOnlyList<LabelRecord> records,
        BoxProcessingRequest request,
        decimal weight) =>
        new(
            Status: BoxProcessingStatus.Success,
            Message: DataLoadedMessage,
            Records: records,
            Weight: weight,
            PrintPlan: _processingPolicy.CreateSuccessPrintPlan(request)
        );
}
