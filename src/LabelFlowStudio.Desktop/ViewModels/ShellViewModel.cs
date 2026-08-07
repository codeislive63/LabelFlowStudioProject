using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Desktop.Navigation;
using System.Windows.Input;

namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// Состояние постоянной оболочки приложения.
/// Навигация и режим автоматической линии намеренно разделены:
/// открытие ручной обработки или настроек не выключает автоматику.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private AppSection _currentSection = AppSection.Work;
    private ViewModelBase _currentSectionViewModel;

    public ShellViewModel(
        MainViewModel work,
        WorkSectionViewModel workSection,
        JournalViewModel journal,
        SettingsViewModel settings)
    {
        Work = work ?? throw new ArgumentNullException(nameof(work));
        WorkSection = workSection ?? throw new ArgumentNullException(nameof(workSection));
        Journal = journal ?? throw new ArgumentNullException(nameof(journal));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));

        if (!ReferenceEquals(Work, WorkSection.Work))
        {
            throw new ArgumentException(
                "WorkSectionViewModel must reference the shell MainViewModel instance.",
                nameof(workSection));
        }

        _currentSectionViewModel = WorkSection;

        NavigateToWorkCommand = new RelayCommand(() => NavigateTo(AppSection.Work));
        NavigateToManualCommand = new RelayCommand(() => NavigateTo(AppSection.Manual));
        NavigateToJournalCommand = new RelayCommand(() => NavigateTo(AppSection.Journal));
        NavigateToSettingsCommand = new RelayCommand(OpenSettingsSection);
    }

    public MainViewModel Work { get; }

    public WorkSectionViewModel WorkSection { get; }

    public JournalViewModel Journal { get; }

    public SettingsViewModel Settings { get; }

    public AppSection CurrentSection
    {
        get => _currentSection;
        private set
        {
            if (!SetProperty(ref _currentSection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsWorkSectionOpen));
            OnPropertyChanged(nameof(IsManualSectionOpen));
            OnPropertyChanged(nameof(IsJournalSectionOpen));
            OnPropertyChanged(nameof(IsSettingsSectionOpen));
            OnPropertyChanged(nameof(CurrentSectionTitle));
        }
    }

    public ViewModelBase CurrentSectionViewModel
    {
        get => _currentSectionViewModel;
        private set => SetProperty(ref _currentSectionViewModel, value);
    }

    public bool IsWorkSectionOpen => CurrentSection == AppSection.Work;

    public bool IsManualSectionOpen => CurrentSection == AppSection.Manual;

    public bool IsJournalSectionOpen => CurrentSection == AppSection.Journal;

    public bool IsSettingsSectionOpen => CurrentSection == AppSection.Settings;

    public string CurrentSectionTitle => CurrentSection switch
    {
        AppSection.Manual => "Ручная обработка",
        AppSection.Journal => "Журнал",
        AppSection.Settings => "Настройки",
        _ => string.Empty
    };

    public ICommand NavigateToWorkCommand { get; }

    public ICommand NavigateToManualCommand { get; }

    public ICommand NavigateToJournalCommand { get; }

    public ICommand NavigateToSettingsCommand { get; }

    private void OpenSettingsSection()
    {
        if (CurrentSection != AppSection.Settings)
        {
            Settings.RefreshDraftFromActive();
        }

        NavigateTo(AppSection.Settings);
    }

    private void NavigateTo(AppSection section)
    {
        CurrentSectionViewModel = section switch
        {
            AppSection.Work => WorkSection,
            AppSection.Manual => WorkSection.Manual,
            AppSection.Journal => Journal,
            AppSection.Settings => Settings,
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
        };

        CurrentSection = section;
    }
}
