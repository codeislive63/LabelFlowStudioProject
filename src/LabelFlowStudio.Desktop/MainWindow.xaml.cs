using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LabelFlowStudio.Desktop.ViewModels;

namespace LabelFlowStudio.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel mainViewModel)
    {
        InitializeComponent();
        DataContext = mainViewModel;
    }

    private void TenamTextBox_PreviewTextInput(object sender, TextCompositionEventArgs eventArgs)
    {
        eventArgs.Handled = !IsDigitsOnly(eventArgs.Text);
    }

    private void TenamTextBox_PreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Space)
        {
            eventArgs.Handled = true;
        }
    }

    private void TenamTextBox_Pasting(object sender, DataObjectPastingEventArgs eventArgs)
    {
        if (!eventArgs.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            eventArgs.CancelCommand();
            return;
        }

        var text = eventArgs.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;

        if (!IsDigitsOnly(text))
        {
            eventArgs.CancelCommand();
        }
    }

    private static bool IsDigitsOnly(string text)
    {
        return !string.IsNullOrEmpty(text) && text.All(char.IsDigit);
    }

    private void RecordsGrid_LoadingRow(object sender, DataGridRowEventArgs eventArgs)
    {
        eventArgs.Row.Header = (eventArgs.Row.GetIndex() + 1).ToString();
    }

    private void RecordsGrid_Sorting(object sender, DataGridSortingEventArgs eventArgs)
    {
        var grid = (DataGrid) sender;

        grid.Dispatcher.InvokeAsync(() =>
        {
            foreach (var item in grid.Items)
            {
                if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                {
                    row.Header = (row.GetIndex() + 1).ToString();
                }
            }
        });
    }
}
