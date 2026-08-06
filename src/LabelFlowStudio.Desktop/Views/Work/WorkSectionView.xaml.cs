using LabelFlowStudio.Desktop.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LabelFlowStudio.Desktop.Views.Work;

/// <summary>
/// Представление рабочего раздела. Обработчики здесь отвечают только за WPF-фокус,
/// валидацию клавиатуры и визуальную нумерацию строк.
/// </summary>
public partial class WorkSectionView : UserControl
{
    private MainViewModel? _subscribedViewModel;

    public WorkSectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnAnyButtonClick), handledEventsToo: true);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeFromViewModel();

        if (e.NewValue is MainViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => UnsubscribeFromViewModel();

    private void UnsubscribeFromViewModel()
    {
        if (_subscribedViewModel is null)
        {
            return;
        }

        _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsBusy)
            && sender is MainViewModel { IsBusy: false })
        {
            _ = RequestPrimaryInputFocusAsync();
        }
    }

    private void WorkSectionView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_subscribedViewModel is null && DataContext is MainViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        _ = RequestPrimaryInputFocusAsync();
    }

    private void WorkSectionView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _ = RequestPrimaryInputFocusAsync();
        }
    }

    private void OnAnyButtonClick(object sender, RoutedEventArgs e) => _ = RequestPrimaryInputFocusAsync();

    internal async Task RequestPrimaryInputFocusAsync()
    {
        if (!IsVisible
            || DataContext is not MainViewModel { IsAutomaticMode: false }
            || Window.GetWindow(this) is not { IsActive: true })
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase focused && !ReferenceEquals(focused, TenamTextBox))
        {
            return;
        }

        await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Input);

        if (!IsVisible
            || !TenamTextBox.IsVisible
            || DataContext is not MainViewModel { IsAutomaticMode: false }
            || Window.GetWindow(this) is not { IsActive: true }
            || Keyboard.FocusedElement is TextBoxBase focusedAfterAwait
               && !ReferenceEquals(focusedAfterAwait, TenamTextBox))
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

        if ((e.Key == Key.Return || e.Key == Key.Enter)
            && DataContext is MainViewModel viewModel
            && !string.IsNullOrWhiteSpace(TenamTextBox.Text))
        {
            viewModel.ReceiveTenamFromScanner(TenamTextBox.Text);
            e.Handled = true;
        }
    }

    private void TenamTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)
            || e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text
            || !IsDigitsOnly(text))
        {
            e.CancelCommand();
        }
    }

    private static bool IsDigitsOnly(string text) =>
        !string.IsNullOrEmpty(text) && text.All(char.IsDigit);

    private void RecordsGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }

    private async void RecordsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        var grid = (DataGrid)sender;
        await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Loaded);

        foreach (var item in grid.Items)
        {
            if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
            {
                row.Header = (row.GetIndex() + 1).ToString();
            }
        }
    }
}
