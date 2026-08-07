namespace LabelFlowStudio.Desktop.Printing;

public sealed record PrintSettingsValidationIssue(string PropertyName, string Message);

public sealed class PrintSettingsValidationResult
{
    public PrintSettingsValidationResult(IEnumerable<PrintSettingsValidationIssue> issues)
    {
        Issues = issues.ToArray();
    }

    public IReadOnlyList<PrintSettingsValidationIssue> Issues { get; }

    public bool IsValid => Issues.Count == 0;

    public string Message => Issues.FirstOrDefault()?.Message ?? string.Empty;

    public string GetMessage(string propertyName) =>
        Issues.FirstOrDefault(issue => issue.PropertyName == propertyName)?.Message ?? string.Empty;
}

public sealed class PrintSettingsValidator
{
    public const int MinimumCopies = 1;
    public const int MaximumCopies = 99;

    public PrintSettingsValidationResult Validate(
        PrintSettings settings,
        IReadOnlyCollection<string> installedPrinters)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(installedPrinters);

        var issues = new List<PrintSettingsValidationIssue>();
        var installed = new HashSet<string>(
            installedPrinters.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);

        ValidatePrinter(
            settings.PrintEndLabelEnabled,
            settings.EndLabelPrinterName,
            installed,
            nameof(PrintSettings.EndLabelPrinterName),
            "торцевых этикеток",
            issues);

        ValidatePrinter(
            settings.PrintStuffingSheetEnabled,
            settings.StuffingSheetPrinterName,
            installed,
            nameof(PrintSettings.StuffingSheetPrinterName),
            "листов сброса",
            issues);

        ValidateCopies(
            settings.EndLabelCopies,
            nameof(PrintSettings.EndLabelCopies),
            "торцевых этикеток",
            issues);

        ValidateCopies(
            settings.StuffingSheetCopies,
            nameof(PrintSettings.StuffingSheetCopies),
            "листов сброса",
            issues);

        return new PrintSettingsValidationResult(issues);
    }

    private static void ValidatePrinter(
        bool isEnabled,
        string? printerName,
        IReadOnlySet<string> installedPrinters,
        string propertyName,
        string roleName,
        ICollection<PrintSettingsValidationIssue> issues)
    {
        if (!isEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(printerName))
        {
            issues.Add(new PrintSettingsValidationIssue(
                propertyName,
                $"Выберите принтер для {roleName}."));
            return;
        }

        if (!installedPrinters.Contains(printerName.Trim()))
        {
            issues.Add(new PrintSettingsValidationIssue(
                propertyName,
                $"Выбранный принтер для {roleName} не найден в Windows."));
        }
    }

    private static void ValidateCopies(
        int copies,
        string propertyName,
        string roleName,
        ICollection<PrintSettingsValidationIssue> issues)
    {
        if (copies is >= MinimumCopies and <= MaximumCopies)
        {
            return;
        }

        issues.Add(new PrintSettingsValidationIssue(
            propertyName,
            $"Количество копий для {roleName} должно быть от {MinimumCopies} до {MaximumCopies}."));
    }
}
