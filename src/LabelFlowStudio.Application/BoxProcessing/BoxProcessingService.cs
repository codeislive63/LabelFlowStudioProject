using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.BoxProcessing;

/// <summary>
/// Обрабатывает отсканированный короб и определяет сценарий печати
/// </summary>
public sealed class BoxProcessingService : IBoxProcessingService
{
    private const string EmptyTenamMessage = "TENAM пустой";
    private const string DataNotFoundMessage = "Данных по коробу не найдено";
    private const string AutoModeWithoutWeightMessage = "Нет веса в БД. Авто-режим: печатаю пустой лист сброса";
    private const string ManualModeWithoutWeightMessage = "Нет веса в БД. Поставьте короб на весы";
    private const string DataLoadedMessage = "Данные загружены";

    private readonly ILabelRepository _labelRepository;

    public BoxProcessingService(ILabelRepository labelRepository)
    {
        _labelRepository = labelRepository ?? throw new ArgumentNullException(nameof(labelRepository));
    }

    /// <summary>
    /// Выполняет обработку данных короба и возвращает итоговое действие для печати
    /// </summary>
    public async Task<BoxProcessingResponse> ProcessAsync(BoxProcessingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedTenam = NormalizeTenam(request.Tenam);

        if (string.IsNullOrWhiteSpace(normalizedTenam))
        {
            return CreateErrorResponse(EmptyTenamMessage);
        }

        var records = await _labelRepository.GetByTenamAsync(normalizedTenam, cancellationToken);

        if (records.Count == 0)
        {
            return CreateNotFoundResponse(records);
        }

        if (!TryGetValidWeight(records, out var weight))
        {
            return request.Mode == WorkMode.Automatic
                ? CreateAutomaticResponseWithoutWeight(records, request)
                : CreateNeedWeightResponse(records);
        }

        return CreateSuccessResponse(records, request, weight);
    }

    // Нормализуем код короба перед обращением в репозиторий
    private static string NormalizeTenam(string tenam) => (tenam ?? string.Empty).Trim();

    // Проверяем, что вес присутствует и больше нуля
    private static bool TryGetValidWeight(IReadOnlyList<LabelRecord> records, out decimal? weight)
    {
        weight = records[0].Brutto;
        return weight.HasValue && weight.Value > 0;
    }

    // Формируем ответ, когда входные данные некорректны
    private static BoxProcessingResponse CreateErrorResponse(string message) =>
        new(
            Status: BoxProcessingStatus.Error,
            Message: message,
            Records: Array.Empty<LabelRecord>(),
            Weight: null,
            ShouldPrintDropSheet: false,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: false
        );

    // Формируем ответ, когда записи по коробу не найдены
    private static BoxProcessingResponse CreateNotFoundResponse(IReadOnlyList<LabelRecord> records) =>
        new(
            Status: BoxProcessingStatus.NotFound,
            Message: DataNotFoundMessage,
            Records: records,
            Weight: null,
            ShouldPrintDropSheet: false,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: false
        );

    // Формируем ответ для авто-режима без веса
    private static BoxProcessingResponse CreateAutomaticResponseWithoutWeight(
        IReadOnlyList<LabelRecord> records,
        BoxProcessingRequest request) =>
        new(
            Status: BoxProcessingStatus.Success,
            Message: AutoModeWithoutWeightMessage,
            Records: records,
            Weight: null,
            ShouldPrintDropSheet: false,
            ShouldPrintEmptyDropSheet: request.ShouldPrintStuffingSheet,
            ShouldPrintEndLabels: request.ShouldPrintEndLabels
        );

    // Формируем ответ для ручного режима без веса
    private static BoxProcessingResponse CreateNeedWeightResponse(IReadOnlyList<LabelRecord> records) =>
        new(
            Status: BoxProcessingStatus.NeedWeight,
            Message: ManualModeWithoutWeightMessage,
            Records: records,
            Weight: null,
            ShouldPrintDropSheet: false,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: false
        );

    // Формируем успешный ответ, когда данные и вес получены
    private static BoxProcessingResponse CreateSuccessResponse(
        IReadOnlyList<LabelRecord> records,
        BoxProcessingRequest request,
        decimal? weight) =>
        new(
            Status: BoxProcessingStatus.Success,
            Message: DataLoadedMessage,
            Records: records,
            Weight: weight,
            ShouldPrintDropSheet: request.ShouldPrintStuffingSheet,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: request.ShouldPrintEndLabels
        );
}
