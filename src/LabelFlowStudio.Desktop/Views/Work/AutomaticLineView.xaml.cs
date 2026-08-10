using LabelFlowStudio.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LabelFlowStudio.Desktop.Views.Work;

/// <summary>
/// Monitoring surface for automatic mode. The timer refreshes the read-only
/// equipment projection and detects a local calendar-day rollover; processing
/// remains owned by the existing view model.
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
        await Task.WhenAll(
            RefreshEquipmentStatusAsync(),
            RefreshStatisticsAsync());
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

        await Task.WhenAll(
            RefreshEquipmentStatusAsync(),
            RefreshStatisticsIfLocalDayChangedAsync());
    }

    private Task RefreshEquipmentStatusAsync()
    {
        return DataContext is AutomaticLineViewModel viewModel
            ? viewModel.RefreshEquipmentStatusAsync()
            : Task.CompletedTask;
    }

    private Task RefreshStatisticsAsync()
    {
        return DataContext is AutomaticLineViewModel viewModel
            ? viewModel.RefreshStatisticsAsync()
            : Task.CompletedTask;
    }

    private Task RefreshStatisticsIfLocalDayChangedAsync()
    {
        return DataContext is AutomaticLineViewModel viewModel
            ? viewModel.RefreshStatisticsIfLocalDayChangedAsync()
            : Task.CompletedTask;
    }
}
