using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using LabelFlowStudio.Desktop.ViewModels;

namespace LabelFlowStudio.Desktop;

public partial class MainWindow : Window
{
    private const int ScanSequenceRestartThresholdMilliseconds = 250;
    private const int ScanBufferTimeoutMilliseconds = 900;

    private readonly DispatcherTimer _scanBufferTimer;
    private string _scanBuffer = string.Empty;
    private DateTime _lastScanCharUtc = DateTime.MinValue;

    public MainWindow(MainViewModel mainViewModel)
    {
        InitializeComponent();
        DataContext = mainViewModel;

        _scanBufferTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ScanBufferTimeoutMilliseconds)
        };
        _scanBufferTimer.Tick += ScanBufferTimer_Tick;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        TenamTextBox.Focus();
        TenamTextBox.SelectAll();
    }

    private void TenamTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs eventArgs)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    // --- Ограничение TENAM (ручной ввод) ---
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

    // --- Глобальный ввод со сканера-клавиатуры (без фокуса в TENAM) ---
    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs eventArgs)
    {
        if (ShouldIgnoreGlobalScannerInput())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(eventArgs.Text))
        {
            return;
        }

        if (!IsDigitsOnly(eventArgs.Text))
        {
            return;
        }

        if (DataContext is MainViewModel viewModel && viewModel.IsBusy)
        {
            return;
        }

        AppendScanDigits(eventArgs.Text);
        eventArgs.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (ShouldIgnoreGlobalScannerInput())
        {
            return;
        }

        if (eventArgs.Key != Key.Return && eventArgs.Key != Key.Enter)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_scanBuffer))
        {
            return;
        }

        if (DataContext is MainViewModel viewModel && !viewModel.IsBusy)
        {
            viewModel.ReceiveTenamFromScanner(_scanBuffer);
        }

        ClearScanBuffer();
        eventArgs.Handled = true;
    }

    private bool ShouldIgnoreGlobalScannerInput()
    {
        // если фокус уже в любом TextBox – не перехватываем (иначе ломаем ручной ввод)
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return true;
        }

        // если фокус именно в TENAM – тоже не перехватываем (там Enter уже привязан к команде)
        if (ReferenceEquals(Keyboard.FocusedElement, TenamTextBox))
        {
            return true;
        }

        return false;
    }

    private void AppendScanDigits(string digits)
    {
        var nowUtc = DateTime.UtcNow;

        if ((nowUtc - _lastScanCharUtc) > TimeSpan.FromMilliseconds(ScanSequenceRestartThresholdMilliseconds))
        {
            _scanBuffer = string.Empty;
        }

        _lastScanCharUtc = nowUtc;

        _scanBufferTimer.Stop();
        _scanBufferTimer.Start();

        _scanBuffer += digits;

        if (DataContext is MainViewModel viewModel)
        {
            // чтобы в поле всегда отображалась собранная строка (без "частей" в разных контролах)
            viewModel.Tenam = _scanBuffer;
        }
    }

    private void ScanBufferTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _scanBufferTimer.Stop();

        // если скан не завершился Enter-ом – очищаем буфер и поле, чтобы не оставлять "полуTENAM"
        if (DataContext is MainViewModel viewModel && viewModel.Tenam == _scanBuffer)
        {
            viewModel.Tenam = string.Empty;
        }

        ClearScanBuffer();
    }

    private void ClearScanBuffer()
    {
        _scanBuffer = string.Empty;
        _lastScanCharUtc = DateTime.MinValue;
    }

    // --- Нумерация строк DataGrid ---
    private void RecordsGrid_LoadingRow(object sender, DataGridRowEventArgs eventArgs)
    {
        eventArgs.Row.Header = (eventArgs.Row.GetIndex() + 1).ToString();
    }

    private void RecordsGrid_Sorting(object sender, DataGridSortingEventArgs eventArgs)
    {
        var grid = (DataGrid)sender;

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

    private void RecordsGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (string.Equals(e.PropertyName, "DeliveryCityRaw", StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, "DeliveryStreetRaw", StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, "DeliveryCity", StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, "DeliveryStreet", StringComparison.Ordinal))
        {
            e.Cancel = true;
            return;
        }

        if (e.Column is DataGridTextColumn textColumn)
        {
            var elementStyle = new Style(typeof(TextBlock));
            elementStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left));
            elementStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            textColumn.ElementStyle = elementStyle;
        }
    }

    // ===========================
    // ✅ TitleBar handlers (NEW)
    // ===========================

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // double-click => maximize/restore
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        try
        {
            // Если окно было развернуто, при перетаскивании аккуратно восстанавливаем
            if (WindowState == WindowState.Maximized)
            {
                var mousePos = e.GetPosition(this);
                var screenPos = PointToScreen(mousePos);

                var restoreWidth = RestoreBounds.Width;
                var restoreHeight = RestoreBounds.Height;

                WindowState = WindowState.Normal;

                // позиционируем так, чтобы курсор оставался над окном
                Left = screenPos.X - (mousePos.X / ActualWidth) * restoreWidth;
                Top = screenPos.Y - (mousePos.Y / ActualHeight) * restoreHeight;
            }

            DragMove();
        }
        catch
        {
            // DragMove иногда кидает исключение, если поймать странный момент мыши — не критично
        }
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
