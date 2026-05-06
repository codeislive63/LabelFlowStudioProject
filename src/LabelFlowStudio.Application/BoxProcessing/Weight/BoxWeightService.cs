using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Core.Abstractions;

namespace LabelFlowStudio.Application.BoxProcessing.Weight;

/// <summary>
/// Сохраняет вес короба в базе данных
/// </summary>
public sealed class BoxWeightService : IBoxWeightService
{
    private const string EmptyTenamMessage = "TENAM пустой";
    private const string InvalidWeightMessage = "Вес должен быть больше нуля";
    private const string WeightNotSavedMessage = "Не удалось сохранить вес в БД";

    private readonly ILabelRepository _labelRepository;

    public BoxWeightService(ILabelRepository labelRepository)
    {
        _labelRepository = labelRepository ?? throw new ArgumentNullException(nameof(labelRepository));
    }

    /// <summary>
    /// Сохраняет введенный вручную вес короба в базе данных
    /// </summary>
    public async Task<BoxWeightUpdateResult> UpdateWeightAsync(
        string tenam,
        decimal weight,
        CancellationToken cancellationToken)
    {
        var normalizedTenam = NormalizeTenam(tenam);

        if (string.IsNullOrWhiteSpace(normalizedTenam))
        {
            return BoxWeightUpdateResult.Failure(EmptyTenamMessage);
        }

        if (weight <= 0)
        {
            return BoxWeightUpdateResult.Failure(InvalidWeightMessage);
        }

        var isUpdated = await _labelRepository.UpdateBruttoByTenamAsync(
            normalizedTenam,
            weight,
            cancellationToken);

        return isUpdated
            ? BoxWeightUpdateResult.Success()
            : BoxWeightUpdateResult.Failure(WeightNotSavedMessage);
    }

    // Нормализует код короба перед сохранением веса
    private static string NormalizeTenam(string tenam) => (tenam ?? string.Empty).Trim();
}
