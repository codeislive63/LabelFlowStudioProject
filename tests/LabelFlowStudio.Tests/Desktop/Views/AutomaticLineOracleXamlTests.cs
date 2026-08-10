using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Desktop.Views.Work;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LabelFlowStudio.Application.Tests.Desktop.Views;

[Collection(WpfApplicationCollection.Name)]
public sealed class AutomaticLineOracleXamlTests
{
    [Fact]
    public void OracleCard_UsesBoundSemanticStatesAndConstrainedContentWidth()
    {
        WpfApplicationTestHost.Run(() =>
        {
            using var work = new MainViewModel(
                new NoOpProcessingService(),
                new NoOpWeightService(),
                new FakeScanner(),
                NullLogger<MainViewModel>.Instance);
            SetWorkModeWithoutPersistence(work, WorkMode.Automatic);

            using var automatic = new AutomaticLineViewModel(
                work,
                () => new AutomaticLineEquipmentSnapshot(true, false, false));
            automatic.RefreshEquipmentStatus();

            var view = new AutomaticLineView { DataContext = automatic };
            ApplyLayout(view);

            var content = Assert.IsType<Grid>(view.FindName("AutomaticLineContent"));
            var card = Assert.IsType<Border>(view.FindName("DatabaseEquipmentCard"));
            var dot = Assert.IsType<Ellipse>(view.FindName("DatabaseStatusDot"));
            var statusText = Assert.IsType<TextBlock>(view.FindName("DatabaseStatusText"));

            Assert.InRange(content.MaxWidth, 1280, 1440);
            AssertOraclePresentation(
                view,
                work,
                card,
                dot,
                statusText,
                OracleConnectionState.Unknown,
                "Не проверено",
                "Запрос к базе данных в текущем запуске ещё не выполнялся.",
                "AutoLineNeutralBrush");
            AssertOraclePresentation(
                view,
                work,
                card,
                dot,
                statusText,
                OracleConnectionState.Checking,
                "Проверка…",
                "Выполняется запрос к базе данных.",
                "AutoLineBlueBrush");
            AssertOraclePresentation(
                view,
                work,
                card,
                dot,
                statusText,
                OracleConnectionState.Connected,
                "Подключено",
                "Последний запрос к базе данных выполнен успешно.",
                "AutoLineSuccessBrush");
            AssertOraclePresentation(
                view,
                work,
                card,
                dot,
                statusText,
                OracleConnectionState.Error,
                "Нет соединения",
                "Не удалось получить данные из базы данных.",
                "AutoLineErrorBrush");

            Assert.Equal(AutomaticLineState.Error, automatic.LineState);
        });
    }

    private static void AssertOraclePresentation(
        AutomaticLineView view,
        MainViewModel work,
        Border card,
        Ellipse dot,
        TextBlock statusText,
        OracleConnectionState state,
        string expectedText,
        string safeDetail,
        string expectedBrushKey)
    {
        SetOracleState(work, state, safeDetail);
        ApplyLayout(view);

        Assert.Equal(expectedText, statusText.Text);
        Assert.Equal(safeDetail, card.ToolTip);
        Assert.Equal(
            GetBrushColor(expectedBrushKey),
            Assert.IsType<SolidColorBrush>(dot.Fill).Color);
    }

    private static Color GetBrushColor(string resourceKey) =>
        Assert.IsType<SolidColorBrush>(System.Windows.Application.Current.FindResource(resourceKey)).Color;

    private static void ApplyLayout(FrameworkElement element)
    {
        element.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
        element.Measure(new Size(1440, 900));
        element.Arrange(new Rect(0, 0, 1440, 900));
        element.UpdateLayout();
    }

    private static void SetWorkModeWithoutPersistence(MainViewModel work, WorkMode mode)
    {
        SetField(work, "_currentWorkMode", mode);
        RaisePropertyChanged(work, nameof(MainViewModel.CurrentWorkMode));
    }

    private static void SetOracleState(
        MainViewModel work,
        OracleConnectionState state,
        string detail)
    {
        SetField(work, "_oracleConnectionStatusDetail", detail);
        SetField(work, "_oracleConnectionState", state);
        RaisePropertyChanged(work, nameof(MainViewModel.OracleConnectionStatusDetail));
        RaisePropertyChanged(work, nameof(MainViewModel.OracleConnectionState));
    }

    private static void SetField<T>(MainViewModel work, string fieldName, T value)
    {
        var field = typeof(MainViewModel).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field.SetValue(work, value);
    }

    private static void RaisePropertyChanged(MainViewModel work, string propertyName)
    {
        var method = typeof(ViewModelBase).GetMethod(
            "OnPropertyChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(work, [propertyName]);
    }

    private sealed class NoOpProcessingService : IBoxProcessingService
    {
        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BoxProcessingResponse(
                BoxProcessingStatus.Success,
                "OK",
                [],
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

    private sealed class FakeScanner : IBoxScanner
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
