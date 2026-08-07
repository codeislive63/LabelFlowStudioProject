using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Desktop.Views;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;
using Wpf.Ui;
using UiContentDialog = Wpf.Ui.Controls.ContentDialog;
using UiContentDialogHost = Wpf.Ui.Controls.ContentDialogHost;
using UiContentDialogResult = Wpf.Ui.Controls.ContentDialogResult;
using UiControlAppearance = Wpf.Ui.Controls.ControlAppearance;
using UiIconElement = Wpf.Ui.Controls.IconElement;
using UiNumberBox = Wpf.Ui.Controls.NumberBox;
using UiSnackbarPresenter = Wpf.Ui.Controls.SnackbarPresenter;
using UiToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace LabelFlowStudio.Application.Tests.Desktop.Views;

[Collection(WpfApplicationCollection.Name)]
public sealed class ShellAndSettingsXamlSmokeTests
{
    [Fact]
    public void MainWindow_LoadsEmbeddedNotificationDrawerAndUnifiedDialogHosts()
    {
        WpfApplicationTestHost.Run(() =>
        {
            using var fixture = new ShellFixture();
            var window = new MainWindow(
                fixture.Shell,
                new ContentDialogService(),
                new SnackbarService());
            window.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);

            var drawer = Assert.IsType<Border>(window.FindName("NotificationDrawer"));
            var notifications = Assert.IsType<ListBox>(window.FindName("NotificationsListBox"));
            var filter = Assert.IsType<ComboBox>(window.FindName("NotificationFilterComboBox"));
            var printMenu = Assert.IsType<Menu>(window.FindName("PrintMenu"));
            var printMenuItem = Assert.IsType<MenuItem>(window.FindName("PrintMenuItem"));
            var endLabelEditor = Assert.IsType<MenuItem>(window.FindName("EndLabelEditorMenuItem"));
            var stuffingSheetEditor = Assert.IsType<MenuItem>(window.FindName("StuffingSheetEditorMenuItem"));

            Assert.InRange(drawer.Width, 400, 420);
            Assert.Equal(
                ScrollBarVisibility.Disabled,
                ScrollViewer.GetHorizontalScrollBarVisibility(notifications));
            Assert.Equal("Только проблемы", ((ComboBoxItem)filter.Items[0]).Content);
            Assert.Equal(0, fixture.Work.NotificationTabIndex);
            Assert.Single(printMenu.Items);
            Assert.Equal("Печать", printMenuItem.Header);
            Assert.Equal("Редактор торцевой этикетки", endLabelEditor.Header);
            Assert.Equal("Редактор листа сброса", stuffingSheetEditor.Header);
            Assert.Same(fixture.Work.OpenEndLabelPreviewCommand, endLabelEditor.Command);
            Assert.Same(fixture.Work.OpenStuffingSheetPreviewCommand, stuffingSheetEditor.Command);
            Assert.Null(window.FindName("PrintSettingsButton"));
            Assert.True(window.ExtendsContentIntoTitleBar);
            Assert.Equal("LabelFlowStudio", window.Title);
            Assert.Null(window.FindName("ShellTitleBar"));
            Assert.NotNull(window.FindName("RootContentDialogHost"));
            Assert.NotNull(window.FindName("RootSnackbarPresenter"));
            Assert.Empty(FindVisualChildren<Popup>(window));

            const string longMessage =
                "Очень длинное техническое сообщение об ошибке, которое должно переноситься внутри панели и не создавать горизонтальную прокрутку.";
            AddNotification(
                fixture.Work,
                longMessage,
                NotificationCategory.Error);
            fixture.Work.IsNotificationCenterOpen = true;
            ApplyLayout(window, 1280, 820);

            Assert.Single(fixture.Work.FilteredNotificationsView.Cast<UiNotification>());
            Assert.True(drawer.ActualWidth <= 420);

            var notification = Assert.Single(fixture.Work.FilteredNotificationsView.Cast<UiNotification>());
            var notificationContent = Assert.IsAssignableFrom<FrameworkElement>(
                notifications.ItemTemplate.LoadContent());
            notificationContent.DataContext = notification;
            ApplyLayout(notificationContent, 370, 100);

            var messageText = FindVisualChildren<TextBlock>(notificationContent)
                .Single(text => text.Text == longMessage);
            Assert.Equal(TextWrapping.Wrap, messageText.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, messageText.TextTrimming);
            Assert.InRange(messageText.MaxHeight, 54, 60);
            Assert.Equal(longMessage, messageText.ToolTip);

            var notificationItem = new ListBoxItem
            {
                DataContext = notification,
                IsSelected = true,
                Style = notifications.ItemContainerStyle,
                Content = notificationContent
            };
            ApplyLayout(notificationItem, 380, 110);
            var errorBorder = Assert.IsType<SolidColorBrush>(
                window.FindResource("AutoLineErrorBorderBrush"));
            Assert.Equal(errorBorder.Color, Assert.IsType<SolidColorBrush>(notificationItem.BorderBrush).Color);

            var unreadDot = Assert.Single(FindVisualChildren<Ellipse>(notificationContent));
            Assert.Equal(Visibility.Visible, unreadDot.Visibility);
            fixture.Work.MarkNotificationAsRead(notification);
            window.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
            Assert.Equal(Visibility.Collapsed, unreadDot.Visibility);

            window.Close();
        });
    }

    [Fact]
    public void SettingsView_LoadsReusableEditorWithTwoPrintRolesAndSeparateScales()
    {
        WpfApplicationTestHost.Run(() =>
        {
            using var fixture = new ShellFixture();
            var view = new SettingsView
            {
                DataContext = fixture.Settings
            };
            var window = new Window { Content = view };

            ApplyLayout(view, 1180, 720);

            var editorView = Assert.Single(FindVisualChildren<PrintSettingsEditorView>(view));
            Assert.Equal(2, FindVisualChildren<UiNumberBox>(view).Count());
            Assert.Equal(2, FindVisualChildren<ComboBox>(view).Count());
            Assert.Equal(3, FindVisualChildren<UiToggleSwitch>(view).Count());
            Assert.Equal(
                ScrollBarVisibility.Disabled,
                ScrollViewer.GetHorizontalScrollBarVisibility(
                    Assert.IsType<ScrollViewer>(FindVisualChild<ScrollViewer>(view))));

            var endLabelPrinter = Assert.IsType<ComboBox>(
                editorView.FindName("EndLabelPrinterComboBox"));
            var endLabelCopies = Assert.IsType<UiNumberBox>(
                editorView.FindName("EndLabelCopiesNumberBox"));
            var endLabelStatus = Assert.IsType<Grid>(
                editorView.FindName("EndLabelPrinterStatus"));

            Assert.True(endLabelPrinter.IsEnabled);
            Assert.True(endLabelCopies.IsEnabled);
            Assert.Equal(Visibility.Collapsed, endLabelStatus.Visibility);

            fixture.Settings.Editor.PrintEndLabelEnabled = false;
            view.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);

            Assert.False(endLabelPrinter.IsEnabled);
            Assert.False(endLabelCopies.IsEnabled);
            Assert.Equal(Visibility.Collapsed, endLabelStatus.Visibility);

            fixture.Settings.Editor.PrintEndLabelEnabled = true;
            fixture.Settings.Editor.EndLabelPrinterName = "Missing printer";
            view.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);

            Assert.Equal(Visibility.Visible, endLabelStatus.Visibility);
            Assert.Contains(
                FindLogicalChildren<TextBlock>(endLabelStatus),
                text => text.Text == "Принтер не найден");

            var actionButtons = FindVisualChildren<Button>(view)
                .Where(button => button.Content is "Отменить изменения" or "Сохранить")
                .ToArray();
            Assert.Equal(2, actionButtons.Length);
            Assert.All(actionButtons, button => Assert.IsType<string>(button.Content));

            window.Content = null;
            window.Close();
        });
    }

    [Fact]
    public void MainWindow_Loaded_WithCompleteConfiguration_DoesNotOpenFirstRunDialog()
    {
        WpfApplicationTestHost.Run(() =>
        {
            using var fixture = new ShellFixture();
            var dialogs = new RecordingContentDialogService();
            var window = new MainWindow(fixture.Shell, dialogs, new RecordingSnackbarService());

            InvokeLoaded(window);

            Assert.Equal(0, dialogs.ShowCalls);
            window.Close();
        });
    }

    [Fact]
    public void SuccessfulSettingsSave_ShowsExactlyOneSnackbarAndDoesNotCreateUnreadProblem()
    {
        WpfApplicationTestHost.Run(() =>
        {
            using var fixture = new ShellFixture();
            var snackbar = new RecordingSnackbarService();
            var window = new MainWindow(
                fixture.Shell,
                new RecordingContentDialogService(),
                snackbar);
            var unreadBefore = fixture.Work.UnreadProblemNotificationsCount;
            fixture.Settings.Editor.EndLabelCopies = 4;

            var saved = fixture.Settings.SaveAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.True(saved);
            Assert.Equal(1, snackbar.ShowCalls);
            Assert.Equal("Настройки сохранены", snackbar.LastTitle);
            Assert.Equal("Изменения успешно применены", snackbar.LastMessage);
            Assert.Equal(unreadBefore, fixture.Work.UnreadProblemNotificationsCount);
            Assert.Empty(fixture.Work.Notifications);
            window.Close();
        });
    }

    [Fact]
    public void MainWindow_Loaded_WithIncompleteConfiguration_OpensUnifiedDialogAndCancelKeepsActive()
    {
        WpfApplicationTestHost.Run(() =>
        {
            var incomplete = CreateCompleteSettings();
            incomplete.EndLabelPrinterName = string.Empty;
            using var fixture = new ShellFixture(incomplete);
            var dialogs = new RecordingContentDialogService
            {
                OnShow = dialog =>
                {
                    if (dialog.Content is ScrollViewer { Content: StackPanel content }
                        && content.Children[0] is PrintSettingsEditorView
                        {
                            DataContext: PrintSettingsEditorViewModel editor
                        })
                    {
                        editor.EndLabelPrinterName = "End printer";
                        editor.EndLabelCopies = 9;
                    }

                    return UiContentDialogResult.None;
                }
            };
            var window = new MainWindow(fixture.Shell, dialogs, new RecordingSnackbarService());

            InvokeLoaded(window);

            Assert.Equal(1, dialogs.ShowCalls);
            var dialog = Assert.IsType<UiContentDialog>(dialogs.LastDialog);
            Assert.Equal("Первичная настройка печати", dialog.Title);
            var scrollViewer = Assert.IsType<ScrollViewer>(dialog.Content);
            var content = Assert.IsType<StackPanel>(scrollViewer.Content);
            var editorView = Assert.IsType<PrintSettingsEditorView>(content.Children[0]);
            var labels = FindLogicalChildren<TextBlock>(editorView)
                .Select(text => text.Text)
                .ToArray();
            Assert.Contains("Торцевые этикетки", labels);
            Assert.Contains("Листы сброса", labels);
            Assert.Contains(
                FindLogicalChildren<UiToggleSwitch>(editorView),
                toggle => Equals(toggle.Content, "Использовать весы"));
            Assert.Empty(fixture.Repository.Active.EndLabelPrinterName);
            Assert.Equal(2, fixture.Repository.Active.EndLabelCopies);
            window.Close();
        });
    }

    private static void InvokeLoaded(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod(
            "MainWindow_Loaded",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(window, [window, new RoutedEventArgs(FrameworkElement.LoadedEvent)]);
        window.Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
    }

    private static void ApplyLayout(FrameworkElement element, double width, double height)
    {
        element.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static void AddNotification(
        MainViewModel work,
        string message,
        NotificationCategory category)
    {
        var method = typeof(MainViewModel).GetMethod(
            "AddNotification",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(work, [message, category]);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject => FindVisualChildren<T>(parent).FirstOrDefault();

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindLogicalChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class ShellFixture : IDisposable
    {
        public ShellFixture(PrintSettings? settings = null)
        {
            Work = new MainViewModel(
                new NoOpProcessingService(),
                new NoOpWeightService(),
                new NoOpScanner(),
                NullLogger<MainViewModel>.Instance);
            Automatic = new AutomaticLineViewModel(
                Work,
                () => new AutomaticLineEquipmentSnapshot(true, true, false));
            WorkSection = new WorkSectionViewModel(
                Work,
                Automatic,
                new ManualProcessingViewModel(Work));
            Repository = new MemorySettingsRepository(settings ?? CreateCompleteSettings());
            Settings = new SettingsViewModel(
                Repository,
                new PrintSettingsEditorFactory(
                    new FixedPrinterCatalog("End printer", "Sheet printer"),
                    new PrintSettingsValidator()));
            Shell = new ShellViewModel(
                Work,
                WorkSection,
                new JournalViewModel(),
                Settings);
        }

        public MainViewModel Work { get; }

        public AutomaticLineViewModel Automatic { get; }

        public WorkSectionViewModel WorkSection { get; }

        public SettingsViewModel Settings { get; }

        public MemorySettingsRepository Repository { get; }

        public ShellViewModel Shell { get; }

        public void Dispose()
        {
            WorkSection.Dispose();
            Automatic.Dispose();
            Work.Dispose();
        }
    }

    private static PrintSettings CreateCompleteSettings() => new()
    {
        EndLabelPrinterName = "End printer",
        StuffingSheetPrinterName = "Sheet printer"
    };

    private sealed class FixedPrinterCatalog(params string[] printers) : IPrinterCatalog
    {
        public IReadOnlyList<string> GetInstalledPrinters() => printers;
    }

    private sealed class MemorySettingsRepository(PrintSettings settings) : IPrintSettingsRepository
    {
        private PrintSettings _settings = settings.Clone();

        public PrintSettings Active => _settings.Clone();

        public PrintSettings? TryLoad() => _settings.Clone();

        public PrintSettings LoadOrDefault() => _settings.Clone();

        public Task SaveAsync(PrintSettings value, CancellationToken cancellationToken)
        {
            _settings = value.Clone();
            return Task.CompletedTask;
        }

        public PrintSettings Update(
            Func<PrintSettings, PrintSettings> update,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settings = update(_settings.Clone()).Clone();
            return _settings.Clone();
        }

        public Task<PrintSettings> UpdateAsync(
            Func<PrintSettings, PrintSettings> update,
            CancellationToken cancellationToken)
        {
            _settings = update(_settings.Clone()).Clone();
            return Task.FromResult(_settings.Clone());
        }
    }

    private sealed class RecordingContentDialogService : IContentDialogService
    {
        private ContentPresenter? _presenter;
        private UiContentDialogHost? _dialogHost;

        public int ShowCalls { get; private set; }

        public UiContentDialog? LastDialog { get; private set; }

        public Func<UiContentDialog, UiContentDialogResult>? OnShow { get; init; }

        public void SetContentPresenter(ContentPresenter contentPresenter) =>
            _presenter = contentPresenter;

        public ContentPresenter GetContentPresenter() =>
            _presenter ?? throw new InvalidOperationException("Presenter is not configured.");

        public void SetDialogHost(ContentPresenter contentPresenter) =>
            _presenter = contentPresenter;

        public void SetDialogHost(UiContentDialogHost contentDialogHost) =>
            _dialogHost = contentDialogHost;

        public ContentPresenter GetDialogHost() =>
            _presenter ?? throw new InvalidOperationException("Legacy dialog host is not configured.");

        public UiContentDialogHost GetDialogHostEx() =>
            _dialogHost ?? throw new InvalidOperationException("Dialog host is not configured.");

        public Task<UiContentDialogResult> ShowAsync(
            UiContentDialog dialog,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShowCalls++;
            LastDialog = dialog;
            return Task.FromResult(OnShow?.Invoke(dialog) ?? UiContentDialogResult.None);
        }
    }

    private sealed class RecordingSnackbarService : ISnackbarService
    {
        private UiSnackbarPresenter? _presenter;

        public TimeSpan DefaultTimeOut { get; set; } = TimeSpan.FromSeconds(3);

        public int ShowCalls { get; private set; }

        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }

        public void SetSnackbarPresenter(UiSnackbarPresenter contentPresenter) =>
            _presenter = contentPresenter;

        public UiSnackbarPresenter GetSnackbarPresenter() =>
            _presenter ?? throw new InvalidOperationException("Snackbar presenter is not configured.");

        public void Show(
            string title,
            string message,
            UiControlAppearance appearance,
            UiIconElement? icon,
            TimeSpan timeout)
        {
            ShowCalls++;
            LastTitle = title;
            LastMessage = message;
        }
    }

    private sealed class NoOpProcessingService : IBoxProcessingService
    {
        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BoxProcessingResponse(
                BoxProcessingStatus.Success,
                "OK",
                Array.Empty<LabelRecord>(),
                null,
                PrintPlan.None));
    }

    private sealed class NoOpWeightService : IBoxWeightService
    {
        public Task<BoxWeightUpdateResult> UpdateWeightAsync(
            string tenam,
            decimal weight,
            CancellationToken cancellationToken) =>
            Task.FromResult(BoxWeightUpdateResult.Success());
    }

    private sealed class NoOpScanner : IBoxScanner
    {
        public event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived
        {
            add { }
            remove { }
        }

        public bool IsRunning => true;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
