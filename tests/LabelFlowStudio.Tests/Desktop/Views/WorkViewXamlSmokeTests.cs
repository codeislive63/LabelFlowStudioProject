using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Desktop.Views.Work;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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

            section.SelectMode(WorkMode.Automatic);
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

            var modeSelector = Assert.IsType<WorkModeSelector>(FindVisualChild<WorkModeSelector>(automaticView));
            var modeButtons = FindVisualChildren<Button>(modeSelector).ToArray();
            Assert.Contains(modeButtons, button => ReferenceEquals(button.Command, work.SwitchToAutomaticModeCommand));
            Assert.Contains(modeButtons, button => ReferenceEquals(button.Command, work.SwitchToManualModeCommand));

            var journalButton = FindVisualChildren<Button>(automaticView)
                .Single(button => FindVisualChildren<TextBlock>(button).Any(text => text.Text == "Открыть журнал"));
            Assert.Same(shell.NavigateToJournalCommand, journalButton.Command);

            section.SelectMode(WorkMode.Manual);
            ApplyLayout(view);

            var manualView = Assert.IsType<ManualProcessingView>(FindVisualChild<ManualProcessingView>(view));
            Assert.NotNull(FindVisualChild<DataGrid>(manualView));

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
