using System.Windows;

namespace LabelFlowStudio.Desktop.Printing;

public partial class PrinterSetupWindow : Window
{
    private const string PreferredEndLabelPrinter = "zebra_torec";
    private const string PreferredStuffingSheetPrinter = "Kyocera ECOSYS PA6000x KX";

    private readonly string[] _printers;
    private readonly PrintSettings _settings;
    private int _stepIndex;

    public PrinterSetupWindow(PrintSettings settings)
    {
        InitializeComponent();

        _settings = settings;
        _printers = PrinterDiscovery.GetInstalledPrinters().ToArray();

        PrintersComboBox.ItemsSource = _printers;

        PrintEndLabelCheckBox.IsChecked = _settings.PrintEndLabelEnabled;
        PrintStuffingSheetCheckBox.IsChecked = _settings.PrintStuffingSheetEnabled;

        if (_printers.Length > 0)
        {
            PrintersComboBox.SelectedItem = ResolveDefaultPrinter(PreferredEndLabelPrinter) ?? _printers[0];
        }

        BackButton.IsEnabled = false;
        UpdateStepUi();
    }

    public PrintSettings ResultSettings => _settings;

    public static async Task<bool> EnsureConfiguredAsync(Window owner, CancellationToken cancellationToken)
    {
        var settings = PrintSettingsStore.LoadOrDefault();

        var window = new PrinterSetupWindow(settings)
        {
            Owner = owner
        };

        var dialogResult = window.ShowDialog();

        if (dialogResult != true)
        {
            return false;
        }

        await PrintSettingsStore.SaveAsync(window.ResultSettings, cancellationToken);
        return true;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_stepIndex <= 0)
        {
            return;
        }

        _stepIndex--;
        UpdateStepUi();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Visibility = Visibility.Collapsed;

        _settings.PrintEndLabelEnabled = PrintEndLabelCheckBox.IsChecked == true;
        _settings.PrintStuffingSheetEnabled = PrintStuffingSheetCheckBox.IsChecked == true;

        var selected = PrintersComboBox.SelectedItem as string;
        var printerRequired = _stepIndex == 0
            ? _settings.PrintEndLabelEnabled
            : _settings.PrintStuffingSheetEnabled;

        if (printerRequired && string.IsNullOrWhiteSpace(selected))
        {
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        if (_stepIndex == 0)
        {
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _settings.EndLabelPrinterName = selected;
            }

            _stepIndex = 1;
            UpdateStepUi();
            return;
        }

        if (!string.IsNullOrWhiteSpace(selected))
        {
            _settings.StuffingSheetPrinterName = selected;
        }

        if (_settings.EndLabelCopies <= 0)
        {
            _settings.EndLabelCopies = 2;
        }

        if (_settings.StuffingSheetCopies <= 0)
        {
            _settings.StuffingSheetCopies = 1;
        }

        DialogResult = true;
        Close();
    }

    private void OnPrintEndLabelChecked(object sender, RoutedEventArgs e)
    {
        _settings.PrintEndLabelEnabled = PrintEndLabelCheckBox.IsChecked == true;

        if (_stepIndex == 0)
        {
            PrintersComboBox.IsEnabled = _settings.PrintEndLabelEnabled;
            ValidationText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnPrintStuffingSheetChecked(object sender, RoutedEventArgs e)
    {
        _settings.PrintStuffingSheetEnabled = PrintStuffingSheetCheckBox.IsChecked == true;

        if (_stepIndex == 1)
        {
            PrintersComboBox.IsEnabled = _settings.PrintStuffingSheetEnabled;
            ValidationText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateStepUi()
    {
        if (_stepIndex == 0)
        {
            StepText.Text = "Шаг 1 из 2";
            TitleText.Text = "Выберите принтер для торцевых этикеток";
            BackButton.IsEnabled = false;
            NextButton.Content = "Далее";

            if (!string.IsNullOrWhiteSpace(_settings.EndLabelPrinterName))
            {
                PrintersComboBox.SelectedItem = _settings.EndLabelPrinterName;
            }
            else if (_printers.Length > 0)
            {
                PrintersComboBox.SelectedItem = ResolveDefaultPrinter(PreferredEndLabelPrinter) ?? _printers[0];
            }

            PrintersComboBox.IsEnabled = _settings.PrintEndLabelEnabled;
            ValidationText.Visibility = Visibility.Collapsed;
            return;
        }

        StepText.Text = "Шаг 2 из 2";
        TitleText.Text = "Выберите принтер для листов сброса";
        BackButton.IsEnabled = true;
        NextButton.Content = "Готово";

        if (!string.IsNullOrWhiteSpace(_settings.StuffingSheetPrinterName))
        {
            PrintersComboBox.SelectedItem = _settings.StuffingSheetPrinterName;
        }
        else if (_printers.Length > 0)
        {
            PrintersComboBox.SelectedItem = ResolveDefaultPrinter(PreferredStuffingSheetPrinter) ?? _printers[0];
        }

        PrintersComboBox.IsEnabled = _settings.PrintStuffingSheetEnabled;
        ValidationText.Visibility = Visibility.Collapsed;
    }

    private string? ResolveDefaultPrinter(string preferredName)
    {
        return _printers.FirstOrDefault(printer => string.Equals(printer, preferredName, StringComparison.OrdinalIgnoreCase));
    }
}
