using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.ViewModels;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class SettingsViewModelTests
{
    private static readonly string[] InstalledPrinters = ["End printer", "Sheet printer"];

    [Fact]
    public void CancelChanges_ReloadsDraftFromActiveWithoutMutatingActive()
    {
        var active = CreateValidSettings();
        var repository = new TestSettingsRepository(active);
        var viewModel = CreateViewModel(repository);
        var editedSession = viewModel.Editor;
        viewModel.Editor.EndLabelCopies = 15;
        viewModel.Editor.UseScales = false;

        viewModel.CancelChanges();

        Assert.NotSame(editedSession, viewModel.Editor);
        Assert.Equal(2, viewModel.Editor.EndLabelCopies);
        Assert.True(viewModel.Editor.UseScales);
        Assert.Equal(2, repository.Active.EndLabelCopies);
        Assert.True(repository.Active.UseScales);
    }

    [Fact]
    public async Task SaveAsync_WithValidDraft_UpdatesActiveSettings()
    {
        var repository = new TestSettingsRepository(CreateValidSettings());
        var viewModel = CreateViewModel(repository);
        var feedback = new List<SettingsFeedbackEventArgs>();
        viewModel.FeedbackRequested += (_, eventArgs) => feedback.Add(eventArgs);
        viewModel.Editor.EndLabelCopies = 5;
        viewModel.Editor.StuffingSheetCopies = 6;
        viewModel.Editor.UseScales = false;

        var saved = await viewModel.SaveAsync(CancellationToken.None);

        Assert.True(saved);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal(5, repository.Active.EndLabelCopies);
        Assert.Equal(6, repository.Active.StuffingSheetCopies);
        Assert.False(repository.Active.UseScales);
        Assert.False(viewModel.HasInlineError);
        var successFeedback = Assert.Single(feedback);
        Assert.Equal(SettingsFeedbackKind.Success, successFeedback.Kind);
        Assert.Equal("Изменения успешно применены", successFeedback.Message);

        viewModel.Editor.EndLabelCopies = 17;
        viewModel.CancelChanges();

        Assert.Equal(5, viewModel.Editor.EndLabelCopies);
    }

    [Fact]
    public async Task SaveAsync_PreservesLatestWorkModeAndManualAutoprint()
    {
        var active = CreateValidSettings();
        active.WorkMode = WorkMode.Manual;
        active.ManualScanAutoPrintEndLabelEnabled = false;
        var repository = new TestSettingsRepository(active);
        var viewModel = CreateViewModel(repository);
        viewModel.Editor.EndLabelCopies = 11;

        var runtimeUpdate = repository.Active;
        runtimeUpdate.WorkMode = WorkMode.Automatic;
        runtimeUpdate.ManualScanAutoPrintEndLabelEnabled = true;
        repository.ReplaceActive(runtimeUpdate);

        var saved = await viewModel.SaveAsync(CancellationToken.None);

        Assert.True(saved);
        Assert.Equal(11, repository.Active.EndLabelCopies);
        Assert.Equal(WorkMode.Automatic, repository.Active.WorkMode);
        Assert.True(repository.Active.ManualScanAutoPrintEndLabelEnabled);
    }

    [Fact]
    public async Task SaveAsync_WhenProcessingIsBusy_IsBlockedAndRetainsDraft()
    {
        var repository = new TestSettingsRepository(CreateValidSettings());
        var isBusy = true;
        var viewModel = CreateViewModel(repository, () => isBusy);
        var editor = viewModel.Editor;
        editor.EndLabelCopies = 9;

        var saved = await viewModel.SaveAsync(CancellationToken.None);

        Assert.False(saved);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Same(editor, viewModel.Editor);
        Assert.Equal(9, viewModel.Editor.EndLabelCopies);
        Assert.Equal(2, repository.Active.EndLabelCopies);
        Assert.True(viewModel.HasInlineError);
        Assert.Contains("обрабатывается текущий короб", viewModel.InlineErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_WhenProcessingStartsAtRepositoryGate_IsBlockedBeforeMutation()
    {
        var isBusy = false;
        var repository = new TestSettingsRepository(CreateValidSettings())
        {
            BeforeUpdate = () => isBusy = true
        };
        var viewModel = CreateViewModel(repository, () => isBusy);
        viewModel.Editor.EndLabelCopies = 9;

        var saved = await viewModel.SaveAsync(CancellationToken.None);

        Assert.False(saved);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal(2, repository.Active.EndLabelCopies);
        Assert.Equal(9, viewModel.Editor.EndLabelCopies);
        Assert.Contains("обрабатывается текущий короб", viewModel.InlineErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_WhenRepositoryFails_LeavesActiveAndDraftUnchanged()
    {
        var repository = new TestSettingsRepository(CreateValidSettings())
        {
            UpdateException = new IOException("disk unavailable")
        };
        var viewModel = CreateViewModel(repository);
        var editor = viewModel.Editor;
        editor.EndLabelCopies = 13;

        var saved = await viewModel.SaveAsync(CancellationToken.None);

        Assert.False(saved);
        Assert.Same(editor, viewModel.Editor);
        Assert.Equal(13, viewModel.Editor.EndLabelCopies);
        Assert.Equal(2, repository.Active.EndLabelCopies);
        Assert.True(viewModel.HasInlineError);
        Assert.Contains("disk unavailable", viewModel.InlineErrorMessage);
    }

    [Fact]
    public void RequiresInitialConfiguration_WhenConfigurationPassesEditorValidator_IsFalse()
    {
        var repository = new TestSettingsRepository(CreateValidSettings());
        var viewModel = CreateViewModel(repository);

        Assert.False(viewModel.RequiresInitialConfiguration);
        Assert.Null(viewModel.CreateInitialEditorIfRequired());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Printer removed from Windows")]
    public void RequiresInitialConfiguration_WhenConfigurationFailsEditorValidator_IsTrue(
        string printerName)
    {
        var active = CreateValidSettings();
        active.EndLabelPrinterName = printerName;
        var repository = new TestSettingsRepository(active);
        var viewModel = CreateViewModel(repository);

        Assert.True(viewModel.RequiresInitialConfiguration);
        Assert.NotNull(viewModel.CreateInitialEditorIfRequired());
    }

    [Fact]
    public void DiscardedInitialEditor_DoesNotMutateActiveSettings()
    {
        var active = CreateValidSettings();
        active.EndLabelPrinterName = string.Empty;
        var repository = new TestSettingsRepository(active);
        var viewModel = CreateViewModel(repository);

        var initialEditor = viewModel.CreateInitialEditorIfRequired();
        Assert.NotNull(initialEditor);
        initialEditor.EndLabelPrinterName = "End printer";
        initialEditor.EndLabelCopies = 21;
        initialEditor.UseScales = false;

        Assert.Empty(repository.Active.EndLabelPrinterName);
        Assert.Equal(2, repository.Active.EndLabelCopies);
        Assert.True(repository.Active.UseScales);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public async Task SaveAsync_WhenEnabledRoleHasNoPrinter_DoesNotCallRepository()
    {
        var repository = new TestSettingsRepository(CreateValidSettings());
        var viewModel = CreateViewModel(repository);
        viewModel.Editor.EndLabelPrinterName = string.Empty;

        var saved = await viewModel.SaveAsync(CancellationToken.None);

        Assert.False(saved);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal("End printer", repository.Active.EndLabelPrinterName);
        Assert.Contains("Выберите принтер", viewModel.InlineErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_WhenSelectedPrinterIsMissing_DoesNotCallRepository()
    {
        var repository = new TestSettingsRepository(CreateValidSettings());
        var viewModel = CreateViewModel(repository);
        viewModel.Editor.StuffingSheetPrinterName = "Missing printer";

        var saved = await viewModel.SaveAsync(CancellationToken.None);

        Assert.False(saved);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.Equal("Sheet printer", repository.Active.StuffingSheetPrinterName);
        Assert.Contains("не найден в Windows", viewModel.InlineErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_RechecksWindowsPrintersAndRejectsOneRemovedAfterOpen()
    {
        var repository = new TestSettingsRepository(CreateValidSettings());
        var catalog = new TestPrinterCatalog(InstalledPrinters);
        var viewModel = CreateViewModel(repository, printerCatalog: catalog);
        catalog.Printers = ["End printer"];
        SettingsFeedbackEventArgs? feedback = null;
        viewModel.FeedbackRequested += (_, eventArgs) => feedback = eventArgs;

        var saved = await viewModel.SaveAsync(CancellationToken.None);

        Assert.False(saved);
        Assert.Equal(0, repository.UpdateCalls);
        Assert.True(catalog.ReadCount >= 2);
        Assert.True(viewModel.Editor.IsStuffingSheetPrinterMissing);
        Assert.Contains("не найден в Windows", viewModel.InlineErrorMessage);
        Assert.Equal(SettingsFeedbackKind.Error, feedback?.Kind);
    }

    private static SettingsViewModel CreateViewModel(
        IPrintSettingsRepository repository,
        Func<bool>? isProcessing = null,
        IPrinterCatalog? printerCatalog = null)
    {
        var factory = new PrintSettingsEditorFactory(
            printerCatalog ?? new TestPrinterCatalog(InstalledPrinters),
            new PrintSettingsValidator());
        return new SettingsViewModel(repository, factory, isProcessing);
    }

    private static PrintSettings CreateValidSettings() => new()
    {
        PrintEndLabelEnabled = true,
        EndLabelPrinterName = "End printer",
        EndLabelCopies = 2,
        PrintStuffingSheetEnabled = true,
        StuffingSheetPrinterName = "Sheet printer",
        StuffingSheetCopies = 1,
        UseScales = true,
        ManualScanAutoPrintEndLabelEnabled = false,
        WorkMode = WorkMode.Manual
    };

    private sealed class TestPrinterCatalog : IPrinterCatalog
    {
        public TestPrinterCatalog(IReadOnlyList<string> printers)
        {
            Printers = printers;
        }

        public IReadOnlyList<string> Printers { get; set; }

        public int ReadCount { get; private set; }

        public IReadOnlyList<string> GetInstalledPrinters()
        {
            ReadCount++;
            return Printers;
        }
    }

    private sealed class TestSettingsRepository : IPrintSettingsRepository
    {
        private PrintSettings _active;

        public TestSettingsRepository(PrintSettings active)
        {
            _active = active.Clone();
        }

        public int UpdateCalls { get; private set; }

        public Exception? UpdateException { get; init; }

        public Action? BeforeUpdate { get; init; }

        public PrintSettings Active => _active.Clone();

        public PrintSettings? TryLoad() => _active.Clone();

        public PrintSettings LoadOrDefault() => _active.Clone();

        public Task SaveAsync(PrintSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _active = settings.Clone();
            return Task.CompletedTask;
        }

        public PrintSettings Update(
            Func<PrintSettings, PrintSettings> update,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateCalls++;
            BeforeUpdate?.Invoke();

            if (UpdateException is not null)
            {
                throw UpdateException;
            }

            var updated = update(_active.Clone());
            _active = updated.Clone();
            return _active.Clone();
        }

        public Task<PrintSettings> UpdateAsync(
            Func<PrintSettings, PrintSettings> update,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateCalls++;

            if (UpdateException is not null)
            {
                throw UpdateException;
            }

            var updated = update(_active.Clone());
            _active = updated.Clone();
            return Task.FromResult(_active.Clone());
        }

        public void ReplaceActive(PrintSettings settings)
        {
            _active = settings.Clone();
        }
    }
}
