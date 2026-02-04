using LabelFlowStudio.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Windows;

namespace LabelFlowStudio.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddUserSecrets<App>(optional: true);
                })
                .ConfigureServices((context, services) =>
                {
                    // Database
                    services.AddLabelFlowDataAccess(context.Configuration);

                    // UI
                    services.AddSingleton<MainWindow>();
                })
                .Build();

        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _host.StopAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // не успели корректно остановиться за таймаут
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                _host.Dispose();
            }
        }

        base.OnExit(e);
    }
}
