using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Desktop.Printing;

namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// One isolated edit session. It owns a cloned draft and a snapshot of the
/// installed Windows printers captured when the editor was opened.
/// </summary>
public sealed class PrintSettingsEditorViewModel : ViewModelBase
{
    private readonly PrintSettingsValidator _validator;
    private IReadOnlyList<string> _installedPrinters;
    private HashSet<string> _installedPrinterSet;
    private readonly PrintSettings _draft;
    private PrintSettingsValidationResult _validation;

    internal PrintSettingsEditorViewModel(
        PrintSettings activeSettings,
        IReadOnlyList<string> installedPrinters,
        PrintSettingsValidator validator)
    {
        ArgumentNullException.ThrowIfNull(activeSettings);
        ArgumentNullException.ThrowIfNull(installedPrinters);
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));

        _draft = activeSettings.Clone();
        _installedPrinters = NormalizeInstalledPrinters(installedPrinters);
        _installedPrinterSet = new HashSet<string>(_installedPrinters, StringComparer.OrdinalIgnoreCase);

        // Keep ComboBox.SelectedItem inside ItemsSource even when Windows returns
        // the same printer with different casing than the persisted value.
        _draft.EndLabelPrinterName = ResolveCatalogName(_draft.EndLabelPrinterName, _installedPrinters);
        _draft.StuffingSheetPrinterName = ResolveCatalogName(
            _draft.StuffingSheetPrinterName,
            _installedPrinters);

        AvailablePrinters = BuildAvailablePrinters(_draft, _installedPrinters);
        _validation = _validator.Validate(_draft, _installedPrinters);
    }

    public IReadOnlyList<string> AvailablePrinters { get; private set; }

    public bool PrintEndLabelEnabled
    {
        get => _draft.PrintEndLabelEnabled;
        set
        {
            if (_draft.PrintEndLabelEnabled == value)
            {
                return;
            }

            _draft.PrintEndLabelEnabled = value;
            OnPropertyChanged();
            RaiseEndLabelPrinterStateChanged();
            Revalidate();
        }
    }

    public bool PrintStuffingSheetEnabled
    {
        get => _draft.PrintStuffingSheetEnabled;
        set
        {
            if (_draft.PrintStuffingSheetEnabled == value)
            {
                return;
            }

            _draft.PrintStuffingSheetEnabled = value;
            OnPropertyChanged();
            RaiseStuffingSheetPrinterStateChanged();
            Revalidate();
        }
    }

    public string EndLabelPrinterName
    {
        get => _draft.EndLabelPrinterName;
        set
        {
            value ??= string.Empty;
            if (_draft.EndLabelPrinterName == value)
            {
                return;
            }

            _draft.EndLabelPrinterName = value;
            OnPropertyChanged();
            RaiseEndLabelPrinterStateChanged();
            Revalidate();
        }
    }

    public string StuffingSheetPrinterName
    {
        get => _draft.StuffingSheetPrinterName;
        set
        {
            value ??= string.Empty;
            if (_draft.StuffingSheetPrinterName == value)
            {
                return;
            }

            _draft.StuffingSheetPrinterName = value;
            OnPropertyChanged();
            RaiseStuffingSheetPrinterStateChanged();
            Revalidate();
        }
    }

    public int EndLabelCopies
    {
        get => _draft.EndLabelCopies;
        set
        {
            if (_draft.EndLabelCopies == value)
            {
                return;
            }

            _draft.EndLabelCopies = value;
            OnPropertyChanged();
            Revalidate();
        }
    }

    public int StuffingSheetCopies
    {
        get => _draft.StuffingSheetCopies;
        set
        {
            if (_draft.StuffingSheetCopies == value)
            {
                return;
            }

            _draft.StuffingSheetCopies = value;
            OnPropertyChanged();
            Revalidate();
        }
    }

    public bool UseScales
    {
        get => _draft.UseScales;
        set
        {
            if (_draft.UseScales == value)
            {
                return;
            }

            _draft.UseScales = value;
            OnPropertyChanged();
        }
    }

    public bool IsAutomaticProcessingEnabled
    {
        get => _draft.WorkMode == WorkMode.Automatic;
        set
        {
            var workMode = value ? WorkMode.Automatic : WorkMode.Manual;
            if (_draft.WorkMode == workMode)
            {
                return;
            }

            _draft.WorkMode = workMode;
            OnPropertyChanged();
        }
    }

    public bool IsValid => _validation.IsValid;

    public string ValidationMessage => _validation.Message;

    public string EndLabelPrinterValidationMessage =>
        _validation.GetMessage(nameof(PrintSettings.EndLabelPrinterName));

    public string StuffingSheetPrinterValidationMessage =>
        _validation.GetMessage(nameof(PrintSettings.StuffingSheetPrinterName));

    public string EndLabelCopiesValidationMessage =>
        _validation.GetMessage(nameof(PrintSettings.EndLabelCopies));

    public string StuffingSheetCopiesValidationMessage =>
        _validation.GetMessage(nameof(PrintSettings.StuffingSheetCopies));

    public bool IsEndLabelPrinterSelected => !string.IsNullOrWhiteSpace(EndLabelPrinterName);

    public bool IsEndLabelPrinterInstalled =>
        PrintEndLabelEnabled
        && IsEndLabelPrinterSelected
        && _installedPrinterSet.Contains(EndLabelPrinterName.Trim());

    public bool IsEndLabelPrinterMissing =>
        PrintEndLabelEnabled && IsEndLabelPrinterSelected && !IsEndLabelPrinterInstalled;

    public string EndLabelPrinterStateText => GetPrinterStateText(
        PrintEndLabelEnabled,
        IsEndLabelPrinterSelected,
        IsEndLabelPrinterInstalled);

    public bool IsStuffingSheetPrinterSelected => !string.IsNullOrWhiteSpace(StuffingSheetPrinterName);

    public bool IsStuffingSheetPrinterInstalled =>
        PrintStuffingSheetEnabled
        && IsStuffingSheetPrinterSelected
        && _installedPrinterSet.Contains(StuffingSheetPrinterName.Trim());

    public bool IsStuffingSheetPrinterMissing =>
        PrintStuffingSheetEnabled
        && IsStuffingSheetPrinterSelected
        && !IsStuffingSheetPrinterInstalled;

    public string StuffingSheetPrinterStateText => GetPrinterStateText(
        PrintStuffingSheetEnabled,
        IsStuffingSheetPrinterSelected,
        IsStuffingSheetPrinterInstalled);

    public PrintSettings CreateDraftSnapshot() => _draft.Clone();

    /// <summary>
    /// Applies only fields owned by this editor to the latest active snapshot.
    /// Manual-screen transient state is not stored here, but the automatic-line
    /// enable flag is a real persisted setting and therefore is merged explicitly.
    /// </summary>
    public PrintSettings MergeWithLatestActive(PrintSettings latestActive)
    {
        ArgumentNullException.ThrowIfNull(latestActive);

        var result = latestActive.Clone();
        result.PrintEndLabelEnabled = _draft.PrintEndLabelEnabled;
        result.PrintStuffingSheetEnabled = _draft.PrintStuffingSheetEnabled;
        result.EndLabelPrinterName = _draft.EndLabelPrinterName.Trim();
        result.StuffingSheetPrinterName = _draft.StuffingSheetPrinterName.Trim();
        result.EndLabelCopies = _draft.EndLabelCopies;
        result.StuffingSheetCopies = _draft.StuffingSheetCopies;
        result.UseScales = _draft.UseScales;
        result.WorkMode = _draft.WorkMode;
        return result;
    }

    internal PrintSettingsValidationResult Validate()
    {
        Revalidate();
        return _validation;
    }

    internal void RefreshInstalledPrinters(IReadOnlyList<string> installedPrinters)
    {
        ArgumentNullException.ThrowIfNull(installedPrinters);

        _installedPrinters = NormalizeInstalledPrinters(installedPrinters);
        _installedPrinterSet = new HashSet<string>(_installedPrinters, StringComparer.OrdinalIgnoreCase);
        AvailablePrinters = BuildAvailablePrinters(_draft, _installedPrinters);

        OnPropertyChanged(nameof(AvailablePrinters));
        RaiseEndLabelPrinterStateChanged();
        RaiseStuffingSheetPrinterStateChanged();
        Revalidate();
    }

    private static IReadOnlyList<string> NormalizeInstalledPrinters(
        IEnumerable<string> installedPrinters) =>
        installedPrinters
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> BuildAvailablePrinters(
        PrintSettings settings,
        IReadOnlyList<string> installedPrinters)
    {
        var result = installedPrinters.ToList();

        AddPersistedPrinter(result, settings.EndLabelPrinterName);
        AddPersistedPrinter(result, settings.StuffingSheetPrinterName);

        return result;
    }

    private static void AddPersistedPrinter(ICollection<string> printers, string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName) ||
            printers.Contains(printerName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        printers.Add(printerName);
    }

    private static string ResolveCatalogName(
        string? persistedName,
        IReadOnlyList<string> installedPrinters)
    {
        if (string.IsNullOrWhiteSpace(persistedName))
        {
            return string.Empty;
        }

        return installedPrinters.FirstOrDefault(
                   name => string.Equals(name, persistedName, StringComparison.OrdinalIgnoreCase))
               ?? persistedName;
    }

    private static string GetPrinterStateText(
        bool isEnabled,
        bool isSelected,
        bool isInstalled)
    {
        if (!isEnabled)
        {
            return "Отключено";
        }

        if (!isSelected)
        {
            return "Принтер не выбран";
        }

        return isInstalled ? "Установлен" : "Принтер не найден";
    }

    private void Revalidate()
    {
        _validation = _validator.Validate(_draft, _installedPrinters);
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(EndLabelPrinterValidationMessage));
        OnPropertyChanged(nameof(StuffingSheetPrinterValidationMessage));
        OnPropertyChanged(nameof(EndLabelCopiesValidationMessage));
        OnPropertyChanged(nameof(StuffingSheetCopiesValidationMessage));
    }

    private void RaiseEndLabelPrinterStateChanged()
    {
        OnPropertyChanged(nameof(IsEndLabelPrinterSelected));
        OnPropertyChanged(nameof(IsEndLabelPrinterInstalled));
        OnPropertyChanged(nameof(IsEndLabelPrinterMissing));
        OnPropertyChanged(nameof(EndLabelPrinterStateText));
    }

    private void RaiseStuffingSheetPrinterStateChanged()
    {
        OnPropertyChanged(nameof(IsStuffingSheetPrinterSelected));
        OnPropertyChanged(nameof(IsStuffingSheetPrinterInstalled));
        OnPropertyChanged(nameof(IsStuffingSheetPrinterMissing));
        OnPropertyChanged(nameof(StuffingSheetPrinterStateText));
    }
}
