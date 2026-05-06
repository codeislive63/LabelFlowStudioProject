namespace LabelFlowStudio.Application.BoxProcessing.Weight;

/// <summary>
/// Описывает результат определения веса короба по строкам данных
/// </summary>
public sealed record BoxWeightResolution(
    bool HasWeight,
    bool HasConflict,
    decimal? Weight,
    string Message)
{
    /// <summary>
    /// Создает результат без найденного веса
    /// </summary>
    public static BoxWeightResolution Missing(string message) => new(
        HasWeight: false,
        HasConflict: false,
        Weight: null,
        Message: message
    );

    /// <summary>
    /// Создает результат с найденным весом
    /// </summary>
    public static BoxWeightResolution Found(decimal weight) => new(
        HasWeight: true,
        HasConflict: false,
        Weight: weight,
        Message: string.Empty
    );

    /// <summary>
    /// Создает результат с конфликтом разных весов
    /// </summary>
    public static BoxWeightResolution Conflict(string message) => new(
        HasWeight: false,
        HasConflict: true,
        Weight: null,
        Message: message
    );
}
