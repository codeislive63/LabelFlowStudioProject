namespace LabelFlowStudio.Application.BoxProcessing.Contracts;

/// <summary>
/// Описывает результат сохранения веса короба
/// </summary>
public sealed record BoxWeightUpdateResult(
    bool IsSuccess,
    string Message)
{
    /// <summary>
    /// Создает успешный результат сохранения веса
    /// </summary>
    public static BoxWeightUpdateResult Success(string message = "Вес сохранен в БД") => new(
        IsSuccess: true,
        Message: message
    );

    /// <summary>
    /// Создает неуспешный результат сохранения веса
    /// </summary>
    public static BoxWeightUpdateResult Failure(string message) => new(
        IsSuccess: false,
        Message: message
    );
}
