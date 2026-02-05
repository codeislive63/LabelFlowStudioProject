using LabelFlowStudio.Core.Abstractions;

namespace LabelFlowStudio.App.BoxProcessing;

public sealed class BoxProcessingService : IBoxProcessingService
{
    private readonly ILabelRepository _labelRepository;

    public BoxProcessingService(ILabelRepository labelRepository)
    {
        _labelRepository = labelRepository ?? throw new ArgumentNullException(nameof(labelRepository));
    }

    public async Task<BoxProcessingResponse> ProcessAsync(BoxProcessingRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var tenam = (request.Tenam ?? string.Empty).Trim();
        
        if (string.IsNullOrWhiteSpace(tenam))
        {
            return new BoxProcessingResponse(
                Status: BoxProcessingStatus.Error,
                Message: "TENAM пустой",
                Records: Array.Empty<Core.Models.LabelRecord>(),
                Weight: null,
                ShouldPrintDropSheet: false,
                ShouldPrintEmptyDropSheet: false,
                ShouldPrintEndLabels: false
            );
        }

        var records = await _labelRepository.GetByTenamAsync(tenam, cancellationToken);

        if (records.Count == 0)
        {
            return new BoxProcessingResponse(
                Status: BoxProcessingStatus.NotFound,
                Message: "Данных по коробу не найдено",
                Records: records,
                Weight: null,
                ShouldPrintDropSheet: false,
                ShouldPrintEmptyDropSheet: false,
                ShouldPrintEndLabels: false
            );
        }

        var weightFromDatabase = records[0].Brutto;
        var hasWeight = weightFromDatabase.HasValue && weightFromDatabase.Value > 0;

        if (!hasWeight)
        {
            if (request.Mode == WorkMode.Automatic)
            {
                return new BoxProcessingResponse(
                    Status: BoxProcessingStatus.Success,
                    Message: "Нет веса в БД. Авто-режим: печатаю пустой лист сброса",
                    Records: records,
                    Weight: null,
                    ShouldPrintDropSheet: false,
                    ShouldPrintEmptyDropSheet: true,
                    ShouldPrintEndLabels: request.ShouldPrintEndLabels
                );
            }

            return new BoxProcessingResponse(
                Status: BoxProcessingStatus.NeedWeight,
                Message: "Нет веса в БД. Поставьте короб на весы",
                Records: records,
                Weight: null,
                ShouldPrintDropSheet: false,
                ShouldPrintEmptyDropSheet: false,
                ShouldPrintEndLabels: false
            );
        }

        return new BoxProcessingResponse(
            Status: BoxProcessingStatus.Success,
            Message: "Данные загружены",
            Records: records,
            Weight: weightFromDatabase,
            ShouldPrintDropSheet: true,
            ShouldPrintEmptyDropSheet: false,
            ShouldPrintEndLabels: request.ShouldPrintEndLabels
        );
    }
}
