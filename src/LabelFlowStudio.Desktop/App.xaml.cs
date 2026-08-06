using LabelFlowStudio.Application;
using LabelFlowStudio.Data;
using LabelFlowStudio.Desktop.Logging;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices;
using LabelFlowStudio.Printing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LabelFlowStudio.Desktop;

/// <summary>
/// Точка входа WPF приложения и конфигурация инфраструктуры
/// </summary>
public partial class App : System.Windows.Application
{
    private static readonly TimeSpan MemoryDiagnosticsInterval = TimeSpan.FromMinutes(5);

    private IHost? _host;
    private Timer? _memoryDiagnosticsTimer;

    static App()
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ConfigureGlobalExceptionLogging();
        ConfigureSerilog();
        StartMemoryDiagnostics();

        Log.Information(
            "Software rendering enabled for WPF and WebView2. WPF render mode: {RenderMode}",
            RenderOptions.ProcessRenderMode);

        try
        {
            _host = BuildHost();
            _host.Start();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application failed to start");
            Log.CloseAndFlush();
            throw;
        }

        base.OnStartup(e);
    }


    // Настраивает Serilog для файла и отладочного вывода
    private static void ConfigureSerilog()
    {
        var logDirectory = LogPathResolver.GetLogDirectory();
        var logFilePath = Path.Combine(logDirectory, "LabelFlowStudio-.log");
        var logTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}{NewLine}"
                          + "{Message:lj}{NewLine}" + "{Exception}"
                          + "──────────────────────────────────────────────────────────────{NewLine}";

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
                shared: true,
                outputTemplate: logTemplate)
            .CreateLogger();
    }

    // Создает и конфигурирует DI host приложения
    private static IHost BuildHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: false);
                config.AddEnvironmentVariables();

#if DEBUG
                config.AddUserSecrets<App>(optional: true);
#endif
            })
            .ConfigureServices((context, services) =>
            {
                services.AddLabelFlowDataAccess(context.Configuration);
                services.AddLabelFlowDevices(context.Configuration);
                services.AddLabelFlowApplication();
                services.AddLabelFlowPrinting(context.Configuration);
                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainViewModel>();
            })
            .UseSerilog()
            .Build();
    }

    // Останавливает host и освобождает ресурсы при завершении приложения
    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_memoryDiagnosticsTimer is not null)
            {
                await _memoryDiagnosticsTimer.DisposeAsync();
                _memoryDiagnosticsTimer = null;
            }

            await SilentHtmlPrinter.ShutdownAsync();

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

    // Регистрирует глобальные обработчики необработанных исключений
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

    private void StartMemoryDiagnostics()
    {
        LogMemoryUsage();

        _memoryDiagnosticsTimer = new Timer(
            static _ => LogMemoryUsage(),
            state: null,
            dueTime: MemoryDiagnosticsInterval,
            period: MemoryDiagnosticsInterval);
    }

    private static void LogMemoryUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();

            var gcInfo = GC.GetGCMemoryInfo();

            Log.Information(
                "Memory diagnostics: working set {WorkingSetMb:F1} MB; private memory {PrivateMemoryMb:F1} MB; " +
                "managed heap {ManagedHeapMb:F1} MB; GC committed {GcCommittedMb:F1} MB; " +
                "fragmented {FragmentedMb:F1} MB; handles {HandleCount}; Gen2 collections {Gen2Collections}",
                ToMegabytes(process.WorkingSet64),
                ToMegabytes(process.PrivateMemorySize64),
                ToMegabytes(gcInfo.HeapSizeBytes),
                ToMegabytes(gcInfo.TotalCommittedBytes),
                ToMegabytes(gcInfo.FragmentedBytes),
                process.HandleCount,
                GC.CollectionCount(2));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Debug(exception, "Failed to collect memory diagnostics");
        }
    }

    private static double ToMegabytes(long bytes) => bytes / (1024d * 1024d);
}
