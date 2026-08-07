using LabelFlowStudio.Desktop;
using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;

namespace LabelFlowStudio.Application.Tests.Desktop;

public sealed class DesktopServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(typeof(MainViewModel))]
    [InlineData(typeof(AutomaticLineViewModel))]
    [InlineData(typeof(ManualProcessingViewModel))]
    [InlineData(typeof(WorkSectionViewModel))]
    [InlineData(typeof(JournalViewModel))]
    [InlineData(typeof(SettingsViewModel))]
    [InlineData(typeof(ShellViewModel))]
    [InlineData(typeof(MainWindow))]
    [InlineData(typeof(IPrintSettingsRepository))]
    [InlineData(typeof(IPrinterCatalog))]
    [InlineData(typeof(IPrintSettingsEditorFactory))]
    [InlineData(typeof(IContentDialogService))]
    [InlineData(typeof(ISnackbarService))]
    public void AddLabelFlowDesktop_RegistersStableShellGraphAsSingleton(Type serviceType)
    {
        var services = new ServiceCollection();

        services.AddLabelFlowDesktop();

        var descriptor = Assert.Single(services, item => item.ServiceType == serviceType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
