using LabelFlowStudio.Desktop.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LabelFlowStudio.Desktop.Views.Work;

/// <summary>
/// Ручная обработка короба. Code-behind ограничен управлением WPF-фокусом,
/// фильтрацией клавиатурного ввода и визуальной нумерацией строк.
/// </summary>
public partial class ManualProcessingView : UserControl
{
    private MainViewModel? _subscribedViewModel;

    public ManualProcessingView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnAnyButtonClick), handledEventsToo: true);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeFromViewModel();

        if (e.NewValue is ManualProcessingViewModel viewModel)
        {
            SubscribeToViewModel(viewModel.Work);

            if (IsLoaded)
            {
                RestoreSortIndicator(viewModel);
            }
        }
    }

    private void SubscribeToViewModel(MainViewModel viewModel)
    {
        _subscribedViewModel = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
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

    private void ManualProcessingView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_subscribedViewModel is null
            && DataContext is ManualProcessingViewModel viewModel)
        {
            SubscribeToViewModel(viewModel.Work);
        }

        if (DataContext is ManualProcessingViewModel presentation)
        {
            RestoreSortIndicator(presentation);
        }

        _ = RequestPrimaryInputFocusAsync();
    }

    private void ManualProcessingView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _ = RequestPrimaryInputFocusAsync();
        }
    }

    private void OnAnyButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && IsDescendantOf(source, ManualPager))
        {
            return;
        }

        _ = RequestPrimaryInputFocusAsync();
    }

    internal async Task RequestPrimaryInputFocusAsync()
    {
        if (!IsVisible
            || _subscribedViewModel is not { IsAutomaticMode: false }
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
            || _subscribedViewModel is not { IsAutomaticMode: false }
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
            && _subscribedViewModel is { } viewModel
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
        var offset = DataContext is ManualProcessingViewModel viewModel
            ? (viewModel.CurrentPage - 1) * viewModel.PageSize
            : 0;

        e.Row.Header = (offset + e.Row.GetIndex() + 1).ToString();
    }

    private void RecordsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (DataContext is not ManualProcessingViewModel viewModel
            || string.IsNullOrWhiteSpace(e.Column.SortMemberPath))
        {
            return;
        }

        e.Handled = true;

        var nextDirection = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var column in RecordsGrid.Columns)
        {
            column.SortDirection = null;
        }

        e.Column.SortDirection = nextDirection;
        viewModel.ApplySort(e.Column.SortMemberPath, nextDirection);

        foreach (var item in RecordsGrid.Items)
        {
            if (RecordsGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
            {
                RecordsGrid_LoadingRow(RecordsGrid, new DataGridRowEventArgs(row));
            }
        }
    }

    private void RestoreSortIndicator(ManualProcessingViewModel viewModel)
    {
        foreach (var column in RecordsGrid.Columns)
        {
            column.SortDirection = string.Equals(
                column.SortMemberPath,
                viewModel.SortPropertyName,
                StringComparison.Ordinal)
                    ? viewModel.SortDirection
                    : null;
        }
    }

    private static bool IsDescendantOf(DependencyObject source, DependencyObject ancestor)
    {
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject value) =>
        value is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(value)
            : LogicalTreeHelper.GetParent(value);
}
