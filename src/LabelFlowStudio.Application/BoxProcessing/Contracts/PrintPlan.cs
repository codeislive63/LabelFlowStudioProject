namespace LabelFlowStudio.Application.BoxProcessing.Contracts;

/// <summary>
/// Описывает план печати после обработки короба
/// </summary>
public sealed record PrintPlan(
    bool PrintDropSheet,
    bool PrintEmptyDropSheet,
    bool PrintEndLabels)
{
    /// <summary>
    /// Пустой план печати без заданий
    /// </summary>
    public static PrintPlan None { get; } = new(
        PrintDropSheet: false,
        PrintEmptyDropSheet: false,
        PrintEndLabels: false
    );

    /// <summary>
    /// Возвращает признак отсутствия заданий на печать
    /// </summary>
    public bool IsEmpty => !PrintDropSheet && !PrintEmptyDropSheet && !PrintEndLabels;
}