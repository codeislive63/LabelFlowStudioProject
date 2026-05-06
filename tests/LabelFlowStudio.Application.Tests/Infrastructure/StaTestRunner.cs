using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace LabelFlowStudio.Application.Tests.Infrastructure;

public static class StaTestRunner
{
    public static void Run(Action action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    public static T Run<T>(Func<T> func)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        T? result = default;
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return result!;
    }

    public static Task RunAsync(Func<Task> action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        Exception? exception = null;

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;

            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));

            try
            {
                var task = action();

                task.ContinueWith(
                    completedTask =>
                    {
                        if (completedTask.Exception is not null)
                        {
                            exception = completedTask.Exception.InnerException ?? completedTask.Exception;
                        }
                        else if (completedTask.IsCanceled)
                        {
                            exception = new TaskCanceledException(completedTask);
                        }

                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception caught)
            {
                exception = caught;
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            }

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        return Task.CompletedTask;
    }
}
