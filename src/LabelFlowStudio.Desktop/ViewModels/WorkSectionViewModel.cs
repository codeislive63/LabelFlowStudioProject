using LabelFlowStudio.Application.BoxProcessing.Contracts;
using System.ComponentModel;

namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// Выбирает presentation-модель рабочего экрана независимо от навигации shell.
/// Сам режим и команды его смены по-прежнему принадлежат <see cref="MainViewModel"/>.
/// </summary>
public sealed class WorkSectionViewModel : ViewModelBase, IDisposable
{
    private WorkMode _currentMode;
    private ViewModelBase _currentModeViewModel;
    private bool _disposed;

    public WorkSectionViewModel(
        MainViewModel work,
        AutomaticLineViewModel automatic,
        ManualProcessingViewModel manual)
    {
        Work = work ?? throw new ArgumentNullException(nameof(work));
        Automatic = automatic ?? throw new ArgumentNullException(nameof(automatic));
        Manual = manual ?? throw new ArgumentNullException(nameof(manual));
        _currentMode = Work.CurrentWorkMode;
        _currentModeViewModel = ResolveModeViewModel(_currentMode);

        Work.PropertyChanged += OnWorkPropertyChanged;
    }

    public MainViewModel Work { get; }

    public AutomaticLineViewModel Automatic { get; }

    public ManualProcessingViewModel Manual { get; }

    public ViewModelBase CurrentModeViewModel
    {
        get => _currentModeViewModel;
        private set => SetProperty(ref _currentModeViewModel, value);
    }

    public WorkMode CurrentMode
    {
        get => _currentMode;
        private set
        {
            if (!SetProperty(ref _currentMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAutomaticMode));
            OnPropertyChanged(nameof(IsManualMode));
        }
    }

    public bool IsAutomaticMode => CurrentMode == WorkMode.Automatic;

    public bool IsManualMode => CurrentMode == WorkMode.Manual;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Work.PropertyChanged -= OnWorkPropertyChanged;
        _disposed = true;
    }

    private void OnWorkPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (!string.IsNullOrEmpty(eventArgs.PropertyName)
            && eventArgs.PropertyName != nameof(MainViewModel.CurrentWorkMode))
        {
            return;
        }

        SelectMode(Work.CurrentWorkMode);
    }

    internal void SelectMode(WorkMode mode)
    {
        CurrentModeViewModel = ResolveModeViewModel(mode);
        CurrentMode = mode;
    }

    private ViewModelBase ResolveModeViewModel(WorkMode mode) => mode switch
    {
        WorkMode.Automatic => Automatic,
        WorkMode.Manual => Manual,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}
