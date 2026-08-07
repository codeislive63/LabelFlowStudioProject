using System.ComponentModel;
using System.Windows.Input;
using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Desktop.Printing;

namespace LabelFlowStudio.Desktop.ViewModels;

public enum SettingsFeedbackKind
{
    Success,
    Error
}

public sealed class SettingsFeedbackEventArgs : EventArgs
{
    public SettingsFeedbackEventArgs(SettingsFeedbackKind kind, string message)
    {
        Kind = kind;
        Message = message;
    }

    public SettingsFeedbackKind Kind { get; }

    public string Message { get; }
}

public sealed record PrintSettingsSaveResult(
    bool IsSuccess,
    string Message,
    PrintSettings? SavedSettings = null);

/// <summary>
/// Coordinates independent print-settings drafts. Persisted/runtime settings are
/// replaced only after an atomic save has completed successfully.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private const string BusySaveMessage =
        "Настройки печати нельзя применить, пока обрабатывается текущий короб. Дождитесь завершения операции.";

    private readonly IPrintSettingsRepository _repository;
    private readonly IPrintSettingsEditorFactory _editorFactory;
    private readonly Func<bool> _isProcessing;
    private readonly Action<PrintSettings> _applyRuntimeSettings;
    private readonly AsyncCommand _saveCommand;
    private readonly RelayCommand _cancelCommand;

    private PrintSettingsEditorViewModel _editor;
    private string _inlineErrorMessage = string.Empty;
    private bool _isSaving;

    /// <summary>
    /// Kept for XAML smoke tests and lightweight shell tests. Production DI uses
    /// the constructor that receives MainViewModel and the registered services.
    /// </summary>
    public SettingsViewModel()
        : this(
            new PrintSettingsStoreRepository(),
            new PrintSettingsEditorFactory(
                new WindowsPrinterCatalog(),
                new PrintSettingsValidator()),
            static () => false,
            static _ => { })
    {
    }

    public SettingsViewModel(
        MainViewModel mainViewModel,
        IPrintSettingsRepository repository,
        IPrintSettingsEditorFactory editorFactory)
        : this(
            repository,
            editorFactory,
            () => mainViewModel?.IsBusy == true,
            settings => mainViewModel?.ApplyRuntimeSettings(settings))
    {
        ArgumentNullException.ThrowIfNull(mainViewModel);
        mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
    }

    internal SettingsViewModel(
        IPrintSettingsRepository repository,
        IPrintSettingsEditorFactory editorFactory,
        Func<bool>? isProcessing = null,
        Action<PrintSettings>? applyRuntimeSettings = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _editorFactory = editorFactory ?? throw new ArgumentNullException(nameof(editorFactory));
        _isProcessing = isProcessing ?? (() => false);
        _applyRuntimeSettings = applyRuntimeSettings ?? (_ => { });

        _editor = _editorFactory.Create(_repository.LoadOrDefault());
        _editor.PropertyChanged += OnEditorPropertyChanged;

        _saveCommand = new AsyncCommand(
            async () => { await SaveAsync(CancellationToken.None).ConfigureAwait(true); },
            () => !IsSaving);
        _cancelCommand = new RelayCommand(CancelChanges, () => !IsSaving);
    }

    public event EventHandler<SettingsFeedbackEventArgs>? FeedbackRequested;

    public PrintSettingsEditorViewModel Editor
    {
        get => _editor;
        private set
        {
            if (ReferenceEquals(_editor, value))
            {
                return;
            }

            _editor.PropertyChanged -= OnEditorPropertyChanged;
            _editor = value;
            _editor.PropertyChanged += OnEditorPropertyChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public ICommand SaveCommand => _saveCommand;

    public ICommand CancelCommand => _cancelCommand;

    public string InlineErrorMessage
    {
        get => _inlineErrorMessage;
        private set
        {
            if (SetProperty(ref _inlineErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasInlineError));
            }
        }
    }

    public bool HasInlineError => !string.IsNullOrWhiteSpace(InlineErrorMessage);

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (!SetProperty(ref _isSaving, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanSave));
            _saveCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Allows a first-run dialog to keep its primary button disabled until the
    /// same validation rules used by the Settings page pass.
    /// </summary>
    public bool CanSave => Editor.IsValid && !IsSaving && !_isProcessing();

    public bool RequiresInitialConfiguration => CreateInitialEditorIfRequired() is not null;

    public void RefreshDraftFromActive()
    {
        if (IsSaving)
        {
            return;
        }

        Editor = _editorFactory.Create(_repository.LoadOrDefault());
        ClearMessages();
    }

    public PrintSettingsEditorViewModel CreateEditorSession() =>
        _editorFactory.Create(_repository.LoadOrDefault());

    public PrintSettingsEditorViewModel? CreateInitialEditorIfRequired()
    {
        var editor = CreateEditorSession();
        return editor.IsValid ? null : editor;
    }

    public void CancelChanges()
    {
        RefreshDraftFromActive();
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        if (IsSaving)
        {
            return false;
        }

        IsSaving = true;
        ClearMessages();

        try
        {
            var result = await SaveEditorAsync(Editor, cancellationToken).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                InlineErrorMessage = result.Message;
                return false;
            }

            Editor = _editorFactory.Create(result.SavedSettings!);
            return true;
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Saves any editor session, including the reusable first-run dialog editor.
    /// Canceling such a session requires no rollback because its draft is isolated.
    /// </summary>
    public Task<PrintSettingsSaveResult> SaveEditorAsync(
        PrintSettingsEditorViewModel editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (_isProcessing())
        {
            return Task.FromResult(Failure(BusySaveMessage, notify: true));
        }

        // Windows printers may be added or removed while Settings is open.
        // Refresh immediately before applying the draft, including first run.
        var validation = _editorFactory.RefreshAndValidate(editor);
        if (!validation.IsValid)
        {
            return Task.FromResult(Failure(validation.Message, notify: true));
        }

        try
        {
            if (_isProcessing())
            {
                return Task.FromResult(Failure(BusySaveMessage, notify: true));
            }

            // The production repository performs this tiny atomic replacement
            // synchronously on the UI thread. Scanner callbacks therefore cannot
            // begin a box after this final check but before the active cache swap.
            var settingsToSave = _repository.Update(
                latestActive =>
                {
                    if (_isProcessing())
                    {
                        throw new PrintSettingsApplyBlockedException(BusySaveMessage);
                    }

                    return editor.MergeWithLatestActive(latestActive);
                },
                cancellationToken);

            _applyRuntimeSettings(settingsToSave.Clone());

            const string successMessage = "Изменения успешно применены";
            FeedbackRequested?.Invoke(
                this,
                new SettingsFeedbackEventArgs(SettingsFeedbackKind.Success, successMessage));

            return Task.FromResult(new PrintSettingsSaveResult(
                IsSuccess: true,
                successMessage,
                settingsToSave.Clone()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PrintSettingsApplyBlockedException exception)
        {
            return Task.FromResult(Failure(exception.Message, notify: true));
        }
        catch (Exception exception)
        {
            return Task.FromResult(Failure(
                $"Не удалось сохранить настройки печати: {exception.Message}",
                notify: true));
        }
    }

    private PrintSettingsSaveResult Failure(string message, bool notify)
    {
        if (notify)
        {
            FeedbackRequested?.Invoke(
                this,
                new SettingsFeedbackEventArgs(SettingsFeedbackKind.Error, message));
        }

        return new PrintSettingsSaveResult(IsSuccess: false, message);
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrintSettingsEditorViewModel.IsValid))
        {
            OnPropertyChanged(nameof(CanSave));
        }

    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(CanSave));
        }
    }

    private void ClearMessages()
    {
        InlineErrorMessage = string.Empty;
    }

    private sealed class PrintSettingsApplyBlockedException(string message)
        : InvalidOperationException(message);
}
