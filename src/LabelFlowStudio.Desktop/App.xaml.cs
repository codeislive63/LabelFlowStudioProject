using LabelFlowStudio.Application;
using LabelFlowStudio.Data;
using LabelFlowStudio.Desktop.Logging;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System.IO;
using System.Windows;

namespace LabelFlowStudio.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        ConfigureGlobalExceptionLogging();

        var logDirectory = LogPathResolver.GetLogDirectory();
        var logFilePath = Path.Combine(logDirectory, "LabelFlowStudio-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddUserSecrets<App>(optional: true);
                })
                .ConfigureServices((context, services) =>
                {
                    // Database
                    services.AddLabelFlowDataAccess(context.Configuration);

                    // Devices
                    services.AddLabelFlowDevices(context.Configuration);

                    // Application
                    services.AddLabelFlowApplication();

                    // UI
                    services.AddSingleton<MainWindow>();

                    // ViewModel
                    services.AddSingleton<MainViewModel>();
                })
                .UseSerilog()
                .Build();

            _host.Start();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");
            Log.CloseAndFlush();
            throw;
        }

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is null)
            {
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            try
            {
                await _host.StopAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Warning("Host stop timed out");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Host stop failed");
            }
            finally
            {
                _host.Dispose();
            }
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private static void ConfigureGlobalExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Unhandled exception");
                Log.CloseAndFlush();
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }
}
