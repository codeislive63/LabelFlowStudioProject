using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Desktop.Input;
using LabelFlowStudio.Desktop.Navigation;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Desktop.Views;
using LabelFlowStudio.Desktop.Views.Work;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace LabelFlowStudio.Desktop;

/// <summary>
/// Постоянное окно приложения. Оно отвечает за навигационную оболочку и перевод
/// событий keyboard-wedge устройства в WPF-независимый адаптер ввода.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel _viewModel;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly KeyboardScannerInputAdapter _scannerInput = new();
    private readonly DispatcherTimer _scannerBufferTimer;
    private string _scannerTextMirroredToTenam = string.Empty;
    private bool _isModalInteractionActive;
    private CancellationTokenSource? _settingsSavedToastCancellation;

    public MainWindow(
        ShellViewModel viewModel,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _contentDialogService = contentDialogService
            ?? throw new ArgumentNullException(nameof(contentDialogService));
        _snackbarService = snackbarService
            ?? throw new ArgumentNullException(nameof(snackbarService));

        InitializeComponent();
        DataContext = viewModel;
        _contentDialogService.SetDialogHost(RootContentDialogHost);
        _snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);

        _scannerBufferTimer = new DispatcherTimer
        {
            Interval = _scannerInput.BufferTimeout
        };
        _scannerBufferTimer.Tick += ScannerBufferTimer_Tick;
        _viewModel.PropertyChanged += OnShellPropertyChanged;
        _viewModel.Work.PropertyChanged += OnWorkPropertyChanged;
        _viewModel.Settings.FeedbackRequested += OnSettingsFeedbackRequested;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Settings.CreateInitialEditorIfRequired() is { } initialEditor)
        {
            await ShowInitialPrintSettingsDialogAsync(initialEditor, CancellationToken.None);
        }

        await FocusManualInputAsync();
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        _ = FocusManualInputAsync();
    }

    private async Task FocusManualInputAsync()
    {
        if (_viewModel.CurrentSection != AppSection.Manual
            || FindVisualChild<ManualProcessingView>(this) is not { } manualView)
        {
            return;
        }

        await manualView.RequestPrimaryInputFocusAsync();
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
            || !_viewModel.Work.IsAutomaticMode
            || _viewModel.Work.IsBusy
            || _viewModel.Work.IsNotificationCenterOpen
            || _isModalInteractionActive)
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
        if (e.PropertyName != nameof(ShellViewModel.CurrentSection))
        {
            return;
        }

        ResetUnacceptedScannerSignal();

        if (_viewModel.CurrentSection == AppSection.Manual)
        {
            _ = FocusManualInputAsync();
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

    private async Task<bool> ShowInitialPrintSettingsDialogAsync(
        PrintSettingsEditorViewModel editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var errorText = new System.Windows.Controls.TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        errorText.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty,
            "ErrorBrush");

        var editorView = new PrintSettingsEditorView
        {
            DataContext = editor
        };

        var dialogContent = new StackPanel();
        dialogContent.Children.Add(editorView);
        dialogContent.Children.Add(errorText);

        var scrollViewer = new ScrollViewer
        {
            Content = dialogContent,
            MaxHeight = 570,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var dialog = new ContentDialog(RootContentDialogHost)
        {
            Title = "Первичная настройка печати",
            Content = scrollViewer,
            PrimaryButtonText = "Сохранить и продолжить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
            DialogWidth = 940,
            DialogMaxWidth = 980,
            DialogMaxHeight = 720
        };
        dialog.SetBinding(
            ContentDialog.IsPrimaryButtonEnabledProperty,
            new Binding(nameof(PrintSettingsEditorViewModel.IsValid))
            {
                Source = editor,
                Mode = BindingMode.OneWay
            });

        var allowPrimaryClose = false;
        var saveInProgress = false;
        dialog.Closing += async (_, eventArgs) =>
        {
            if (saveInProgress && !allowPrimaryClose)
            {
                eventArgs.Cancel = true;
                return;
            }

            if (eventArgs.Result != ContentDialogResult.Primary || allowPrimaryClose)
            {
                return;
            }

            eventArgs.Cancel = true;
            saveInProgress = true;
            try
            {
                var result = await _viewModel.Settings.SaveEditorAsync(editor, cancellationToken);
                if (!result.IsSuccess)
                {
                    errorText.Text = result.Message;
                    errorText.Visibility = Visibility.Visible;
                    return;
                }

                allowPrimaryClose = true;
                dialog.Hide(ContentDialogResult.Primary);
            }
            finally
            {
                saveInProgress = false;
            }
        };

        _isModalInteractionActive = true;
        ResetUnacceptedScannerSignal();
        try
        {
            return await _contentDialogService.ShowAsync(dialog, cancellationToken)
                == ContentDialogResult.Primary;
        }
        finally
        {
            _isModalInteractionActive = false;
        }
    }

    private void OnSettingsFeedbackRequested(
        object? sender,
        SettingsFeedbackEventArgs eventArgs)
    {
        if (eventArgs.Kind == SettingsFeedbackKind.Success)
        {
            _ = ShowSettingsSavedToastAsync();
            return;
        }

        _snackbarService.Show(
            "Ошибка настроек",
            eventArgs.Message,
            ControlAppearance.Danger,
            new SymbolIcon(SymbolRegular.ErrorCircle24),
            TimeSpan.FromSeconds(4));
    }

    private async Task ShowSettingsSavedToastAsync()
    {
        _settingsSavedToastCancellation?.Cancel();
        _settingsSavedToastCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        _settingsSavedToastCancellation = cancellation;
        var cancellationToken = cancellation.Token;

        // Сбрасываем предыдущие анимации на случай быстрого повторного сохранения.
        SettingsSavedToast.BeginAnimation(UIElement.OpacityProperty, null);
        SettingsSavedToastTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            null);

        SettingsSavedToast.Visibility = Visibility.Visible;
        SettingsSavedToast.Opacity = 0;
        SettingsSavedToastTranslate.Y = 10;

        var enterEasing = new CubicEase
        {
            EasingMode = EasingMode.EaseOut
        };

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = enterEasing
        };

        var moveIn = new DoubleAnimation
        {
            From = 10,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = enterEasing
        };

        SettingsSavedToast.BeginAnimation(
            UIElement.OpacityProperty,
            fadeIn);

        SettingsSavedToastTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            moveIn);

        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(1800),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var exitEasing = new CubicEase
        {
            EasingMode = EasingMode.EaseIn
        };

        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = exitEasing
        };

        var moveOut = new DoubleAnimation
        {
            From = 0,
            To = 4,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = exitEasing
        };

        fadeOut.Completed += (_, _) =>
            completion.TrySetResult(true);

        SettingsSavedToast.BeginAnimation(
            UIElement.OpacityProperty,
            fadeOut);

        SettingsSavedToastTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            moveOut);

        await completion.Task;

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        SettingsSavedToast.BeginAnimation(
            UIElement.OpacityProperty,
            null);

        SettingsSavedToastTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            null);

        SettingsSavedToast.Visibility = Visibility.Collapsed;
        SettingsSavedToast.Opacity = 0;
        SettingsSavedToastTranslate.Y = 10;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _settingsSavedToastCancellation?.Cancel();
        _settingsSavedToastCancellation?.Dispose();
        _settingsSavedToastCancellation = null;

        _scannerBufferTimer.Stop();
        _scannerBufferTimer.Tick -= ScannerBufferTimer_Tick;

        _viewModel.PropertyChanged -= OnShellPropertyChanged;
        _viewModel.Work.PropertyChanged -= OnWorkPropertyChanged;
        _viewModel.Settings.FeedbackRequested -= OnSettingsFeedbackRequested;

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

    private void NotificationDrawerBackdrop_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Work.IsNotificationCenterOpen = false;
    }

    private void OpenFullJournal_Click(object sender, RoutedEventArgs e)
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
