using LabelFlowStudio.Desktop.Printing;

namespace LabelFlowStudio.Desktop.ViewModels;

public interface IPrintSettingsEditorFactory
{
    PrintSettingsEditorViewModel Create(PrintSettings activeSettings);

    PrintSettingsValidationResult RefreshAndValidate(PrintSettingsEditorViewModel editor);
}

public sealed class PrintSettingsEditorFactory : IPrintSettingsEditorFactory
{
    private readonly IPrinterCatalog _printerCatalog;
    private readonly PrintSettingsValidator _validator;

    public PrintSettingsEditorFactory(
        IPrinterCatalog printerCatalog,
        PrintSettingsValidator validator)
    {
        _printerCatalog = printerCatalog ?? throw new ArgumentNullException(nameof(printerCatalog));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public PrintSettingsEditorViewModel Create(PrintSettings activeSettings)
    {
        ArgumentNullException.ThrowIfNull(activeSettings);
        return new PrintSettingsEditorViewModel(
            activeSettings,
            _printerCatalog.GetInstalledPrinters(),
            _validator);
    }

    public PrintSettingsValidationResult RefreshAndValidate(PrintSettingsEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        editor.RefreshInstalledPrinters(_printerCatalog.GetInstalledPrinters());
        return editor.Validate();
    }
}
