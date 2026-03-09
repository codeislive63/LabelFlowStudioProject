using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LabelFlowStudio.Desktop;

public partial class ManualWeightInputWindow : Window
{
    private bool _isCompletionAccepted;

    public ManualWeightInputWindow(string tenam)
    {
        InitializeComponent();
        PromptMessage = $"Поставьте коробку № {tenam} на весы или введите вес вручную.";
        DataContext = this;
        Loaded += (_, _) =>
        {
            WeightTextBox.Focus();
            WeightTextBox.SelectAll();
        };
        Closing += ManualWeightInputWindow_Closing;
    }

    public string PromptMessage { get; }

    public decimal? EnteredWeight { get; private set; }

    public decimal? ScaleWeight { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseWeight(WeightTextBox.Text, out var weight))
        {
            MessageBox.Show(this,
                "Введите корректный вес. Используйте точку в качестве разделителя дробной части.",
                "Некорректный вес",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        EnteredWeight = weight;
        _isCompletionAccepted = true;
        DialogResult = true;
    }

    public void AcceptScaleWeight(decimal weight)
    {
        if (weight <= 0)
        {
            return;
        }

        ScaleWeight = weight;
        _isCompletionAccepted = true;
        DialogResult = true;
    }

    private void ManualWeightInputWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isCompletionAccepted)
        {
            return;
        }

        e.Cancel = true;
    }

    private void WeightTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        e.Handled = !CanApplyInput(textBox.Text, e.Text, textBox.SelectionStart, textBox.SelectionLength);
    }

    private void WeightTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            e.CancelCommand();
            return;
        }

        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            e.CancelCommand();
            return;
        }

        var pasted = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
        if (!CanApplyInput(textBox.Text, pasted, textBox.SelectionStart, textBox.SelectionLength))
        {
            e.CancelCommand();
        }
    }

    private void WeightTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.OemComma || e.Key == Key.Decimal)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            SaveButton_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private static bool CanApplyInput(string current, string input, int selectionStart, int selectionLength)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (input.Contains(','))
        {
            return false;
        }

        var candidate = current.Remove(selectionStart, selectionLength).Insert(selectionStart, input);
        return candidate.All(ch => char.IsDigit(ch) || ch == '.')
               && candidate.Count(ch => ch == '.') <= 1;
    }

    private static bool TryParseWeight(string text, out decimal weight)
    {
        var normalized = (text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains(','))
        {
            weight = 0;
            return false;
        }

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out weight))
        {
            return false;
        }

        return weight > 0;
    }
}
