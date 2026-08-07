using System.Windows;
using System.Windows.Controls;
using LabelFlowStudio.Desktop.ViewModels;

namespace LabelFlowStudio.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.RefreshDraftFromActive();
        }
    }
}
