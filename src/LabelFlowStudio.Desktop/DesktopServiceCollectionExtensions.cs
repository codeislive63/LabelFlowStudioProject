using LabelFlowStudio.Desktop.Printing;
using LabelFlowStudio.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;

namespace LabelFlowStudio.Desktop;

/// <summary>
/// Регистрирует постоянную оболочку и модели представления desktop-слоя.
/// </summary>
public static class DesktopServiceCollectionExtensions
{
    public static IServiceCollection AddLabelFlowDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPrintSettingsRepository, PrintSettingsStoreRepository>();
        services.AddSingleton<IPrinterCatalog, WindowsPrinterCatalog>();
        services.AddSingleton<PrintSettingsValidator>();
        services.AddSingleton<IPrintSettingsEditorFactory, PrintSettingsEditorFactory>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<AutomaticLineViewModel>();
        services.AddSingleton<ManualProcessingViewModel>();
        services.AddSingleton<WorkSectionViewModel>();
        services.AddSingleton<JournalViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
