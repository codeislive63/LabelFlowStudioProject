using System.Windows.Controls;

namespace LabelFlowStudio.Desktop.Views.Work;

/// <summary>
/// Выбирает presentation для текущего WorkMode. Рабочий UI находится в отдельных
/// AutomaticLineView и ManualProcessingView.
/// </summary>
public partial class WorkSectionView : UserControl
{
    public WorkSectionView()
    {
        InitializeComponent();
    }
}
