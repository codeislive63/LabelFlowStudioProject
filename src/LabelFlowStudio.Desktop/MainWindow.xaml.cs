using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Desktop.Input;
using LabelFlowStudio.Desktop.Navigation;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Desktop.Views.Work;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace LabelFlowStudio.Desktop;

/// <summary>
/// Постоянное окно приложения. Оно отвечает за навигационную оболочку и перевод
/// событий keyboard-wedge устройства в WPF-независимый адаптер ввода.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel _viewModel;
    private readonly KeyboardScannerInputAdapter _scannerInput = new();
    private readonly DispatcherTimer _scannerBufferTimer;
    private string _scannerTextMirroredToTenam = string.Empty;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();
        DataContext = viewModel;

        _scannerBufferTimer = new DispatcherTimer
        {
            Interval = _scannerInput.BufferTimeout
        };
        _scannerBufferTimer.Tick += ScannerBufferTimer_Tick;
        _viewModel.PropertyChanged += OnShellPropertyChanged;
        _viewModel.Work.PropertyChanged += OnWorkPropertyChanged;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await PrinterSetupWindow.EnsureConfiguredAsync(this, CancellationToken.None);
        await FocusWorkInputAsync();
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        _ = FocusWorkInputAsync();
    }

    private async Task FocusWorkInputAsync()
    {
        if (_viewModel.CurrentSection != AppSection.Work
            || FindVisualChild<ManualProcessingView>(this) is not { } workView)
        {
            return;
        }

        await workView.RequestPrimaryInputFocusAsync();
    }

    private async void OnOpenPrintSettingsClick(object sender, RoutedEventArgs e)
    {
        await PrinterSetupWindow.ShowSettingsAsync(this, CancellationToken.None);
        await FocusWorkInputAsync();
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var shouldCapture = ShouldCaptureGlobalScannerInput();
        var result = _scannerInput.ProcessTextInput(
            e.Text,
            GetScannerMode(),
            DateTimeOffset.UtcNow,
            shouldCapture);

        ApplyScannerResult(result);
        e.Handled = result.ShouldHandle;
    }

    private void Window_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is TextBoxBase && !string.IsNullOrEmpty(_scannerInput.Buffer))
        {
            ResetUnacceptedScannerSignal();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var keyKind = e.Key is Key.Return or Key.Enter
            ? KeyboardScannerKeyKind.Enter
            : KeyboardScannerKeyKind.Other;

        var result = _scannerInput.ProcessKeyDown(
            keyKind,
            GetScannerMode(),
            DateTimeOffset.UtcNow,
            ShouldCaptureGlobalScannerInput());

        ApplyScannerResult(result);
        e.Handled = result.ShouldHandle;
    }

    private KeyboardScannerInputMode GetScannerMode() => _viewModel.Work.CurrentWorkMode switch
    {
        WorkMode.Automatic => KeyboardScannerInputMode.Automatic,
        _ => KeyboardScannerInputMode.Manual
    };

    private bool ShouldCaptureGlobalScannerInput()
    {
        if (_viewModel.CurrentSection != AppSection.Work
            || _viewModel.Work.IsBusy
            || _viewModel.Work.IsNotificationCenterOpen)
        {
            return false;
        }

        // Поля настроек и ручного ввода всегда получают обычную клавиатуру.
        return Keyboard.FocusedElement is not TextBoxBase;
    }

    private void ApplyScannerResult(KeyboardScannerInputResult result)
    {
        if (result.Outcome == KeyboardScannerInputOutcome.BufferUpdated)
        {
            _scannerBufferTimer.Stop();
            _scannerBufferTimer.Start();
            _scannerTextMirroredToTenam = result.Buffer;
            _viewModel.Work.Tenam = result.Buffer;
        }
        else if (result.Outcome == KeyboardScannerInputOutcome.BufferCleared)
        {
            _scannerBufferTimer.Stop();
            ClearMirroredScannerText();
        }
        else if (result.Outcome == KeyboardScannerInputOutcome.ScanCompleted)
        {
            _scannerBufferTimer.Stop();
            _scannerTextMirroredToTenam = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(result.CompletedScan))
        {
            _viewModel.Work.ReceiveTenamFromScanner(result.CompletedScan);
        }
    }

    private void ScannerBufferTimer_Tick(object? sender, EventArgs e)
    {
        _scannerBufferTimer.Stop();
        var result = _scannerInput.ExpireBuffer(DateTimeOffset.UtcNow);
        ApplyScannerResult(result);

        if (result.Outcome == KeyboardScannerInputOutcome.Ignored
            && !string.IsNullOrEmpty(_scannerInput.Buffer))
        {
            _scannerBufferTimer.Start();
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentSection)
            && _viewModel.CurrentSection != AppSection.Work)
        {
            ResetUnacceptedScannerSignal();
        }
    }

    private void OnWorkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentWorkMode))
        {
            ResetUnacceptedScannerSignal();
        }
    }

    private void ResetUnacceptedScannerSignal()
    {
        _scannerBufferTimer.Stop();
        _scannerInput.Reset();
        ClearMirroredScannerText();
    }

    private void ClearMirroredScannerText()
    {
        var mirroredText = _scannerTextMirroredToTenam;
        _scannerTextMirroredToTenam = string.Empty;

        if (!string.IsNullOrEmpty(mirroredText)
            && string.Equals(_viewModel.Work.Tenam, mirroredText, StringComparison.Ordinal))
        {
            _viewModel.Work.Tenam = string.Empty;
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _scannerBufferTimer.Stop();
        _scannerBufferTimer.Tick -= ScannerBufferTimer_Tick;
        _viewModel.PropertyChanged -= OnShellPropertyChanged;
        _viewModel.Work.PropertyChanged -= OnWorkPropertyChanged;
        Activated -= MainWindow_Activated;
        Closed -= MainWindow_Closed;
    }

    private void NotificationCenterButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Work.ToggleNotificationCenter();
    }

    private void NotificationCenterCloseButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Work.IsNotificationCenterOpen = false;
    }

    private void NotificationCenterPopup_Closed(object? sender, EventArgs e)
    {
        _viewModel.Work.IsNotificationCenterOpen = false;
    }

    private void NotificationsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { IsKeyboardFocusWithin: true }
            && _viewModel.Work.IsNotificationCenterOpen
            && e.AddedItems.Count > 0
            && e.AddedItems[0] is UiNotification notification)
        {
            _viewModel.Work.MarkNotificationAsRead(notification);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
