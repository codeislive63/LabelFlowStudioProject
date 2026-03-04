using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace LabelFlowStudio.Desktop;

/// <summary>
/// Главное окно приложения
/// </summary>
public partial class MainWindow : Window
{
    private const int ScanBufferTimeoutMilliseconds = 900;

    private readonly DispatcherTimer _scanBufferTimer;
    private string _scanBuffer = string.Empty;

    private const int MaxScannerInterKeyDelayMilliseconds = 60;
    private DateTime _lastScanKeyAtUtc = DateTime.MinValue;

    /// <summary>
    /// Создает главное окно и связывает его с моделью представления
    /// </summary>
    public MainWindow(MainViewModel mainViewModel)
    {
        InitializeComponent();
        DataContext = mainViewModel;

        mainViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsBusy) && !mainViewModel.IsBusy)
            {
                _ = FocusTenamSoonAsync();
            }
        };

        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, __) => _ = FocusTenamSoonAsync()), true);

        _scanBufferTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ScanBufferTimeoutMilliseconds)
        };

        _scanBufferTimer.Tick += ScanBufferTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await PrinterSetupWindow.EnsureConfiguredAsync(this, CancellationToken.None);
        await FocusTenamSoonAsync();
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        _ = FocusTenamSoonAsync();
    }

    private async void OnOpenPrintSettingsClick(object sender, RoutedEventArgs e)
    {
        await PrinterSetupWindow.EnsureConfiguredAsync(this, CancellationToken.None);
    }

    private void OnAutomaticModeMenuItemChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CurrentWorkMode = WorkMode.Automatic;
        }
    }

    private void OnAutomaticModeMenuItemUnchecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CurrentWorkMode = WorkMode.Manual;
        }
    }

    // Переносит фокус в поле TENAM после завершения текущего UI-цикла
    private async Task FocusTenamSoonAsync()
    {
        if (!IsActive)
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase focused && !ReferenceEquals(focused, TenamTextBox))
        {
            return;
        }

        await Task.Yield();

        if (!IsActive)
        {
            return;
        }

        TenamTextBox.Focus();
        Keyboard.Focus(TenamTextBox);
        TenamTextBox.SelectAll();
    }

    private void TenamTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void TenamTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsDigitsOnly(e.Text);
    }

    private void TenamTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.Return || e.Key == Key.Enter) && DataContext is MainViewModel viewModel)
        {
            var tenamFromInput = TenamTextBox.Text;
            if (!string.IsNullOrWhiteSpace(tenamFromInput))
            {
                viewModel.ReceiveTenamFromScanner(tenamFromInput);
                e.Handled = true;
            }
        }
    }

    private void TenamTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            e.CancelCommand();
            return;
        }

        var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
        if (!IsDigitsOnly(text))
        {
            e.CancelCommand();
        }
    }

    private static bool IsDigitsOnly(string text)
    {
        return !string.IsNullOrEmpty(text) && text.All(char.IsDigit);
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsBusy)
        {
            return;
        }

        if (viewModel.IsAutomaticMode)
        {
            if (string.IsNullOrWhiteSpace(e.Text) || !IsDigitsOnly(e.Text))
            {
                e.Handled = true;
                return;
            }

            var nowUtc = DateTime.UtcNow;

            if (_lastScanKeyAtUtc != DateTime.MinValue
                && (nowUtc - _lastScanKeyAtUtc) > TimeSpan.FromMilliseconds(MaxScannerInterKeyDelayMilliseconds))
            {
                ClearScanBuffer();
                e.Handled = true;
                return;
            }

            _lastScanKeyAtUtc = nowUtc;

            AppendScanDigits(e.Text);
            e.Handled = true;
            return;
        }

        if (ShouldIgnoreGlobalScannerInput())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(e.Text) || !IsDigitsOnly(e.Text))
        {
            return;
        }

        AppendScanDigits(e.Text);
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsBusy)
        {
            return;
        }

        // В авто-режиме блокируем любые клавиши, кроме Enter (завершить скан)
        if (viewModel.IsAutomaticMode)
        {
            if (e.Key != Key.Return && e.Key != Key.Enter)
            {
                e.Handled = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(_scanBuffer))
            {
                e.Handled = true;
                return;
            }

            viewModel.ReceiveTenamFromScanner(_scanBuffer);

            ClearScanBuffer();
            _lastScanKeyAtUtc = DateTime.MinValue;

            e.Handled = true;
            return;
        }

        // Ручной режим — старая логика
        if (ShouldIgnoreGlobalScannerInput())
        {
            return;
        }

        if (e.Key != Key.Return && e.Key != Key.Enter)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_scanBuffer))
        {
            return;
        }

        viewModel.ReceiveTenamFromScanner(_scanBuffer);

        ClearScanBuffer();
        e.Handled = true;
    }

    private static bool ShouldIgnoreGlobalScannerInput()
    {
        return Keyboard.FocusedElement is TextBoxBase;
    }

    private void AppendScanDigits(string digits)
    {
        _scanBufferTimer.Stop();
        _scanBufferTimer.Start();

        _scanBuffer += digits;

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Tenam = _scanBuffer;
        }
    }

    private void ScanBufferTimer_Tick(object? sender, EventArgs e)
    {
        _scanBufferTimer.Stop();
        ClearScanBuffer();
        _lastScanKeyAtUtc = DateTime.MinValue;
    }

    private void ClearScanBuffer()
    {
        _scanBuffer = string.Empty;
    }

    private void RecordsGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }

    // Обновляет номера строк после сортировки
    private async void RecordsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        var grid = (DataGrid)sender;

        await Task.Yield();

        foreach (var item in grid.Items)
        {
            if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
            {
                row.Header = (row.GetIndex() + 1).ToString();
            }
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        try
        {
            if (WindowState == WindowState.Maximized)
            {
                var mousePos = e.GetPosition(this);
                var screenPos = PointToScreen(mousePos);

                var restoreWidth = RestoreBounds.Width;
                var restoreHeight = RestoreBounds.Height;

                WindowState = WindowState.Normal;

                Left = screenPos.X - (mousePos.X / ActualWidth) * restoreWidth;
                Top = screenPos.Y - (mousePos.Y / ActualHeight) * restoreHeight;
            }

            DragMove();
        }
        catch
        {
        }
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
