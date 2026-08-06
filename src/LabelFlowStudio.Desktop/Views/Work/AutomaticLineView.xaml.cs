using LabelFlowStudio.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LabelFlowStudio.Desktop.Views.Work;

/// <summary>
/// Monitoring surface for automatic mode. The timer only refreshes the read-only
/// equipment projection; processing remains owned by the existing view model.
/// </summary>
public partial class AutomaticLineView : UserControl
{
    private readonly DispatcherTimer _equipmentRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(5)
    };

    public AutomaticLineView()
    {
        InitializeComponent();
        _equipmentRefreshTimer.Tick += OnEquipmentRefreshTimerTick;
    }

    private async void AutomaticLineView_Loaded(object sender, RoutedEventArgs e)
    {
        _equipmentRefreshTimer.Start();
        await RefreshEquipmentStatusAsync();
    }

    private void AutomaticLineView_Unloaded(object sender, RoutedEventArgs e)
    {
        _equipmentRefreshTimer.Stop();
    }

    private async void OnEquipmentRefreshTimerTick(object? sender, EventArgs e)
    {
        if (DataContext is AutomaticLineViewModel viewModel)
        {
            viewModel.RefreshMonitoringState();
        }

        await RefreshEquipmentStatusAsync();
    }

    private Task RefreshEquipmentStatusAsync()
    {
        return DataContext is AutomaticLineViewModel viewModel
            ? viewModel.RefreshEquipmentStatusAsync()
            : Task.CompletedTask;
    }
}
