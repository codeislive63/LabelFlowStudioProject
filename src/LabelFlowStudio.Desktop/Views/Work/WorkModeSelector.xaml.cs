using System.Windows.Controls;

namespace LabelFlowStudio.Desktop.Views.Work;

/// <summary>
/// Compact selector for the equipment work mode. Mode changes remain delegated to
/// the existing commands on <c>MainViewModel</c>.
/// </summary>
public partial class WorkModeSelector : UserControl
{
    public WorkModeSelector()
    {
        InitializeComponent();
    }
}
