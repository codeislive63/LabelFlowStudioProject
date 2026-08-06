using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class WorkSectionModeSynchronizationTests
{
    [Fact]
    public void CurrentWorkModePropertyChanged_SwitchesStablePresentationModelsInBothDirections()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            SetWorkModeWithoutPersistence(work, WorkMode.Automatic);

            using var automatic = new AutomaticLineViewModel(
                work,
                () => new AutomaticLineEquipmentSnapshot(
                    IsScannerRunning: true,
                    IsPrinterInstalled: false,
                    UseScales: false));
            var manual = new ManualProcessingViewModel(work);
            using var section = new WorkSectionViewModel(work, automatic, manual);

            Assert.Equal(WorkMode.Automatic, section.CurrentMode);
            Assert.Same(automatic, section.CurrentModeViewModel);

            var changedProperties = new List<string?>();
            section.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName is nameof(WorkSectionViewModel.CurrentModeViewModel)
                    or nameof(WorkSectionViewModel.CurrentMode))
                {
                    changedProperties.Add(eventArgs.PropertyName);
                }
            };

            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Manual);

            Assert.Equal(WorkMode.Manual, section.CurrentMode);
            Assert.Same(manual, section.CurrentModeViewModel);
            Assert.Equal(
                [
                    nameof(WorkSectionViewModel.CurrentModeViewModel),
                    nameof(WorkSectionViewModel.CurrentMode)
                ],
                changedProperties);

            changedProperties.Clear();

            RaiseWorkModeChangedWithoutPersistence(work, WorkMode.Automatic);

            Assert.Equal(WorkMode.Automatic, section.CurrentMode);
            Assert.Same(automatic, section.CurrentModeViewModel);
            Assert.Equal(
                [
                    nameof(WorkSectionViewModel.CurrentModeViewModel),
                    nameof(WorkSectionViewModel.CurrentMode)
                ],
                changedProperties);
        });
    }

    private static MainViewModel CreateWorkViewModel() =>
        new(
            new NoOpProcessingService(),
            new NoOpWeightService(),
            new NoOpScanner(),
            NullLogger<MainViewModel>.Instance);

    private static void RaiseWorkModeChangedWithoutPersistence(MainViewModel work, WorkMode mode)
    {
        SetWorkModeWithoutPersistence(work, mode);

        var method = typeof(ViewModelBase).GetMethod(
            "OnPropertyChanged",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method.Invoke(work, [nameof(MainViewModel.CurrentWorkMode)]);
    }

    private static void SetWorkModeWithoutPersistence(MainViewModel work, WorkMode mode)
    {
        var field = typeof(MainViewModel).GetField(
            "_currentWorkMode",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field.SetValue(work, mode);
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
