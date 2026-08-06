using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Desktop.Navigation;
using System.Windows.Input;

namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// Состояние постоянной оболочки приложения. Раздел и режим оборудования намеренно
/// хранятся раздельно: <see cref="CurrentSection"/> не меняет WorkMode рабочего экрана.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private AppSection _currentSection = AppSection.Work;
    private ViewModelBase _currentSectionViewModel;

    public ShellViewModel(
        MainViewModel work,
        JournalViewModel journal,
        SettingsViewModel settings)
    {
        Work = work ?? throw new ArgumentNullException(nameof(work));
        Journal = journal ?? throw new ArgumentNullException(nameof(journal));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _currentSectionViewModel = Work;

        NavigateToWorkCommand = new RelayCommand(() => NavigateTo(AppSection.Work));
        NavigateToJournalCommand = new RelayCommand(() => NavigateTo(AppSection.Journal));
        NavigateToSettingsCommand = new RelayCommand(() => NavigateTo(AppSection.Settings));
    }

    public MainViewModel Work { get; }

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
            OnPropertyChanged(nameof(IsJournalSectionOpen));
            OnPropertyChanged(nameof(IsSettingsSectionOpen));
        }
    }

    public ViewModelBase CurrentSectionViewModel
    {
        get => _currentSectionViewModel;
        private set => SetProperty(ref _currentSectionViewModel, value);
    }

    public bool IsWorkSectionOpen => CurrentSection == AppSection.Work;

    public bool IsJournalSectionOpen => CurrentSection == AppSection.Journal;

    public bool IsSettingsSectionOpen => CurrentSection == AppSection.Settings;

    public ICommand NavigateToWorkCommand { get; }

    public ICommand NavigateToJournalCommand { get; }

    public ICommand NavigateToSettingsCommand { get; }

    private void NavigateTo(AppSection section)
    {
        CurrentSectionViewModel = section switch
        {
            AppSection.Work => Work,
            AppSection.Journal => Journal,
            AppSection.Settings => Settings,
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
        };

        CurrentSection = section;
    }
}
