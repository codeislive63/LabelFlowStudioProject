using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Desktop.Views.Work;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using System.ComponentModel;
using UiProgressRing = Wpf.Ui.Controls.ProgressRing;

namespace LabelFlowStudio.Application.Tests.Desktop.Views;

[Collection(WpfApplicationCollection.Name)]
public sealed class WorkViewXamlSmokeTests
{
    [Fact]
    public void AppResources_LoadWorkSectionAndBothModeViewsWithoutForbiddenAutomaticContent()
    {
        var printerShutdownBefore = IsSilentPrinterShutdownRequested();

        WpfApplicationTestHost.Run(() =>
        {
            var key = new DataTemplateKey(typeof(WorkSectionViewModel));
            var template = Assert.IsType<DataTemplate>(System.Windows.Application.Current.FindResource(key));

            Assert.IsType<WorkSectionView>(template.LoadContent());
            using var work = CreateWorkViewModel();
            using var automatic = CreateAutomaticViewModel(work);
            var manual = new ManualProcessingViewModel(work);
            using var section = new WorkSectionViewModel(work, automatic, manual);
            var view = new WorkSectionView { DataContext = section };
            var shell = new ShellViewModel(
                work,
                section,
                new JournalViewModel(),
                new SettingsViewModel());
            var window = new Window
            {
                DataContext = shell,
                Content = view
            };

            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Automatic);
            const string longEvent =
                "Длинное техническое предупреждение должно переноситься на две строки и не расширять мониторинговый экран по горизонтали.";
            AddNotification(work, longEvent, NotificationCategory.Warning);
            ApplyLayout(view);

            var automaticView = Assert.IsType<AutomaticLineView>(FindVisualChild<AutomaticLineView>(view));
            Assert.Null(FindVisualChild<DataGrid>(automaticView));
            Assert.True(FindVisualChildren<TextBlock>(automaticView).Count(item => item.Text == "–") >= 4);
            Assert.True(FindVisualChildren<TextBlock>(automaticView).Count(item => item.Text == "Нет данных") >= 4);

            var automaticText = FindVisualChildren<TextBlock>(automaticView)
                .Select(item => item.Text)
                .ToArray();
            Assert.DoesNotContain("Автоматический режим выбран", automaticText);
            Assert.DoesNotContain("Записей 55", automaticText);
            Assert.DoesNotContain("Статус: Данные загружены", automaticText);
            Assert.DoesNotContain("ОЖИДАНИЕ", automaticText);
            Assert.Single(
                automaticText,
                text => text == "Последний обработанный короб пока отсутствует");

            var eventText = FindVisualChildren<TextBlock>(automaticView)
                .Single(text => text.Text == longEvent);
            Assert.Equal(TextWrapping.Wrap, eventText.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, eventText.TextTrimming);
            Assert.Equal(longEvent, eventText.ToolTip);
            Assert.Equal(
                ScrollBarVisibility.Disabled,
                FindVisualChildren<ScrollViewer>(automaticView).First().HorizontalScrollBarVisibility);

            var modeSelector = Assert.IsType<WorkModeSelector>(FindVisualChild<WorkModeSelector>(view));
            var modeButtons = FindVisualChildren<Button>(modeSelector).ToArray();
            Assert.Contains(modeButtons, button => ReferenceEquals(button.Command, work.SwitchToAutomaticModeCommand));
            Assert.Contains(modeButtons, button => ReferenceEquals(button.Command, work.SwitchToManualModeCommand));

            var journalButton = FindVisualChildren<Button>(automaticView)
                .Single(button => FindVisualChildren<TextBlock>(button).Any(text => text.Text == "Открыть журнал"));
            Assert.Same(shell.NavigateToJournalCommand, journalButton.Command);

            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Manual);
            work.Tenam = "4430558";
            for (var index = 1; index <= 55; index++)
            {
                work.Records.Add(new LabelRecord
                {
                    Tenam = "4430558",
                    Artnr = index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)
                });
            }
            ApplyLayout(view);

            var manualView = Assert.IsType<ManualProcessingView>(FindVisualChild<ManualProcessingView>(view));
            var recordsGrid = Assert.IsType<DataGrid>(FindVisualChild<DataGrid>(manualView));
            Assert.True(recordsGrid.EnableRowVirtualization);
            Assert.True(recordsGrid.EnableColumnVirtualization);
            Assert.Equal(ScrollBarVisibility.Auto, recordsGrid.HorizontalScrollBarVisibility);
            Assert.Same(manual.PagedRecords, recordsGrid.ItemsSource);
            Assert.Equal(10, recordsGrid.Items.Count);
            Assert.Equal("Показано 1–10 из 55", manual.RangeText);
            Assert.Equal(20, recordsGrid.Columns.Count);
            Assert.All(recordsGrid.Columns, column =>
            {
                Assert.False(string.IsNullOrWhiteSpace(column.SortMemberPath));
                Assert.NotNull(
                    TypeDescriptor.GetProperties(typeof(LabelRecord))[column.SortMemberPath]);
            });

            var manualContent = Assert.IsType<Grid>(manualView.FindName("ManualContent"));
            var tenamInputHost = Assert.IsType<Border>(manualView.FindName("TenamInputHost"));
            var tenamEditorHost = Assert.IsType<StackPanel>(VisualTreeHelper.GetParent(tenamInputHost));
            var tenamTextBox = Assert.IsType<TextBox>(manualView.FindName("TenamTextBox"));
            var tenamPlaceholder = Assert.IsType<TextBlock>(manualView.FindName("ManualTenamPlaceholder"));
            var loadingOverlay = Assert.IsType<Grid>(manualView.FindName("ManualLoadingOverlay"));
            var loadingRing = Assert.IsType<UiProgressRing>(manualView.FindName("ManualLoadingProgressRing"));
            var pageSizeComboBox = Assert.IsType<ComboBox>(manualView.FindName("PageSizeComboBox"));

            Assert.Equal(1440, manualContent.MaxWidth);
            Assert.Equal(880, tenamEditorHost.MaxWidth);
            Assert.Equal("4430558", tenamTextBox.Text);
            Assert.Equal(Visibility.Collapsed, tenamPlaceholder.Visibility);
            Assert.NotNull(tenamTextBox.Foreground);
            Assert.NotNull(tenamTextBox.CaretBrush);
            Assert.NotNull(tenamTextBox.SelectionBrush);
            Assert.NotNull(tenamTextBox.SelectionTextBrush);
            Assert.NotEqual(tenamTextBox.Background, tenamTextBox.Foreground);
            Assert.Equal(0.22, tenamTextBox.SelectionOpacity, precision: 2);
            Assert.Equal(0, Grid.GetRow(loadingOverlay));
            Assert.True(loadingOverlay.IsHitTestVisible);
            Assert.True(loadingRing.IsIndeterminate);
            Assert.Empty(FindVisualChildren<ProgressBar>(manualView));
            Assert.Equal(10, pageSizeComboBox.SelectedItem);
            Assert.Equal(RenderMode.SoftwareOnly, RenderOptions.ProcessRenderMode);

            var printButtons = FindVisualChildren<Button>(manualView)
                .Where(button => button.Content is "Торцевая этикетка" or "Лист сброса")
                .ToArray();
            Assert.Equal(2, printButtons.Length);
            Assert.All(printButtons, button =>
            {
                Assert.Equal(280, button.MaxWidth);
                Assert.IsType<string>(button.Content);
            });

            manual.ApplySort(nameof(LabelRecord.Artnr), ListSortDirection.Descending);
            manualView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var articleColumn = Assert.Single(
                recordsGrid.Columns,
                column => column.SortMemberPath == nameof(LabelRecord.Artnr));
            Assert.Equal(ListSortDirection.Descending, articleColumn.SortDirection);

            manual.NavigateToPage(2);
            ApplyLayout(view);
            var firstVisibleRow = Assert.IsType<DataGridRow>(
                recordsGrid.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal("11", firstVisibleRow.Header);

            window.Content = null;
        });

        Assert.Null(System.Windows.Application.Current);
        Assert.Equal(printerShutdownBefore, IsSilentPrinterShutdownRequested());
    }

    private static void ApplyLayout(FrameworkElement element)
    {
        element.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
        element.Measure(new Size(1280, 760));
        element.Arrange(new Rect(0, 0, 1280, 760));
        element.UpdateLayout();
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        return FindVisualChildren<T>(parent).FirstOrDefault();
    }

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

    private static MainViewModel CreateWorkViewModel()
    {
        return new MainViewModel(
            new NoOpProcessingService(),
            new NoOpWeightService(),
            new FakeScanner(),
            NullLogger<MainViewModel>.Instance);
    }

    private static void AddNotification(
        MainViewModel work,
        string message,
        NotificationCategory category)
    {
        var method = typeof(MainViewModel).GetMethod(
            "AddNotification",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(work, [message, category]);
    }

    private static void RaiseWorkModeChangedWithoutPersistence(MainViewModel work, WorkMode mode)
    {
        var field = typeof(MainViewModel).GetField(
            "_currentWorkMode",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var method = typeof(ViewModelBase).GetMethod(
            "OnPropertyChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.NotNull(method);
        field.SetValue(work, mode);
        method.Invoke(work, [nameof(MainViewModel.CurrentWorkMode)]);
    }

    private static AutomaticLineViewModel CreateAutomaticViewModel(MainViewModel work)
    {
        return new AutomaticLineViewModel(
            work,
            () => new AutomaticLineEquipmentSnapshot(
                IsScannerRunning: true,
                IsPrinterInstalled: false,
                UseScales: false));
    }

    private static bool IsSilentPrinterShutdownRequested()
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var printerType = typeof(PrintSettings).Assembly.GetType(
            "LabelFlowStudio.Desktop.SilentHtmlPrinter",
            throwOnError: true)!;
        var field = printerType.GetField("_shutdownRequested", flags);

        Assert.NotNull(field);
        return Assert.IsType<bool>(field.GetValue(null));
    }

    private sealed class NoOpProcessingService : IBoxProcessingService
    {
        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new BoxProcessingResponse(
                BoxProcessingStatus.Success,
                "OK",
                [],
                null,
                PrintPlan.None));
        }
    }

    private sealed class NoOpWeightService : IBoxWeightService
    {
        public Task<BoxWeightUpdateResult> UpdateWeightAsync(
            string tenam,
            decimal weight,
            CancellationToken cancellationToken) =>
            Task.FromResult(BoxWeightUpdateResult.Success());
    }

    private sealed class FakeScanner : IBoxScanner
    {
        public event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived
        {
            add { }
            remove { }
        }

        public bool IsRunning { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}

[CollectionDefinition("WPF Application smoke tests", DisableParallelization = true)]
public sealed class WpfApplicationCollection
{
    public const string Name = "WPF Application smoke tests";
}
