using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Windows.Threading;

namespace LabelFlowStudio.Application.Tests.Infrastructure;

/// <summary>
/// Runs one real WPF Application on an isolated STA thread so compiled XAML can be
/// instantiated without opening windows or leaving a live dispatcher afterwards.
/// </summary>
public static class WpfApplicationTestHost
{
    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Exception? testException = null;
        using var completed = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            LabelFlowStudio.Desktop.App? application = null;

            try
            {
                application = new LabelFlowStudio.Desktop.App
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
                application.InitializeComponent();
                action();
            }
            catch (Exception exception)
            {
                testException = exception;
            }
            finally
            {
                // Application.Shutdown would execute the production App.OnExit and
                // permanently stop shared printer/logging services in this test process.
                Dispatcher.CurrentDispatcher.InvokeShutdown();
                ResetApplicationSingleton();
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "LabelFlowStudio.XamlSmokeTests"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!completed.Wait(TimeSpan.FromSeconds(20)))
        {
            throw new TimeoutException("WPF smoke test did not finish in time.");
        }

        if (!thread.Join(TimeSpan.FromSeconds(2)))
        {
            throw new TimeoutException("WPF smoke-test thread did not stop in time.");
        }

        if (testException is not null)
        {
            ExceptionDispatchInfo.Capture(testException).Throw();
        }
    }

    private static void ResetApplicationSingleton()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var applicationType = typeof(System.Windows.Application);
        var globalLock = applicationType.GetField("_globalLock", flags)?.GetValue(null)
            ?? throw new InvalidOperationException("WPF Application global lock was not found.");

        lock (globalLock)
        {
            SetStaticField(applicationType, "_appInstance", null, flags);
            SetStaticField(applicationType, "_appCreatedInThisAppDomain", false, flags);
            SetStaticField(applicationType, "_isShuttingDown", false, flags);
        }
    }

    private static void SetStaticField(
        Type declaringType,
        string fieldName,
        object? value,
        BindingFlags flags)
    {
        var field = declaringType.GetField(fieldName, flags)
            ?? throw new InvalidOperationException($"WPF Application field '{fieldName}' was not found.");
        field.SetValue(null, value);
    }

}
