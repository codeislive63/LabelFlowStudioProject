using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Navigation;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class ShellViewModelTests
{
    [Fact]
    public void Constructor_OpensWorkWithExactRegisteredInstance()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            var journal = new JournalViewModel();
            var settings = new SettingsViewModel();

            var shell = new ShellViewModel(work, journal, settings);

            Assert.Equal(AppSection.Work, shell.CurrentSection);
            Assert.True(shell.IsWorkSectionOpen);
            Assert.Same(work, shell.CurrentSectionViewModel);
        });
    }

    [Fact]
    public void Navigation_UsesStableViewModelsAndDoesNotChangeWorkStateOrMode()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            work.Tenam = "4430558";
            work.Records.Add(new LabelRecord { Tenam = "4430558", Artnr = "A-1" });
            var initialMode = work.CurrentWorkMode;

            var journal = new JournalViewModel();
            var settings = new SettingsViewModel();
            var shell = new ShellViewModel(work, journal, settings);

            shell.NavigateToJournalCommand.Execute(null);
            Assert.Equal(AppSection.Journal, shell.CurrentSection);
            Assert.Same(journal, shell.CurrentSectionViewModel);

            shell.NavigateToSettingsCommand.Execute(null);
            Assert.Equal(AppSection.Settings, shell.CurrentSection);
            Assert.Same(settings, shell.CurrentSectionViewModel);

            shell.NavigateToWorkCommand.Execute(null);
            Assert.Same(work, shell.CurrentSectionViewModel);
            Assert.Equal("4430558", work.Tenam);
            Assert.Single(work.Records);
            Assert.Equal(initialMode, work.CurrentWorkMode);
        });
    }

    [Fact]
    public void Navigation_ToCurrentSection_DoesNotRaisePropertyChanged()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            var shell = new ShellViewModel(work, new JournalViewModel(), new SettingsViewModel());
            var changedProperties = new List<string?>();
            shell.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

            shell.NavigateToWorkCommand.Execute(null);

            Assert.Empty(changedProperties);
        });
    }

    [Fact]
    public void AppSection_ContainsOnlyIndependentTopLevelSections()
    {
        Assert.Equal(
            [AppSection.Work, AppSection.Journal, AppSection.Settings],
            Enum.GetValues<AppSection>());
    }

    private static MainViewModel CreateWorkViewModel()
    {
        return new MainViewModel(
            new NoOpProcessingService(),
            new NoOpWeightService(),
            new NoOpScanner(),
            NullLogger<MainViewModel>.Instance);
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
            CancellationToken cancellationToken)
        {
            return Task.FromResult(BoxWeightUpdateResult.Success());
        }
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
