using System.Windows;
using LabelFlowStudio.Desktop.ViewModels;

namespace LabelFlowStudio.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel mainViewModel)
    {
        InitializeComponent();
        DataContext = mainViewModel;
    }
}
