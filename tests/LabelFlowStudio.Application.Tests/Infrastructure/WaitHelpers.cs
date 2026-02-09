using System.Diagnostics;

namespace LabelFlowStudio.Application.Tests.Infrastructure;

public static class WaitHelpers
{
    public static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(10);
        var stopwatch = Stopwatch.StartNew();

        while (!predicate())
        {
            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException($"Condition was not met within {timeout}.");
            }

            await Task.Delay(interval);
        }
    }
}
