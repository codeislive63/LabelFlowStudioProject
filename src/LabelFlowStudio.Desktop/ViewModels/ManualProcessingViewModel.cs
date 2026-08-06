namespace LabelFlowStudio.Desktop.ViewModels;

/// <summary>
/// Presentation-модель ручного экрана. Она сохраняет единый экземпляр
/// <see cref="MainViewModel"/>, поэтому существующие команды и состояние обработки
/// не дублируются в UI-слое.
/// </summary>
public sealed class ManualProcessingViewModel : ViewModelBase
{
    public ManualProcessingViewModel(MainViewModel work)
    {
        Work = work ?? throw new ArgumentNullException(nameof(work));
    }

    public MainViewModel Work { get; }
}
