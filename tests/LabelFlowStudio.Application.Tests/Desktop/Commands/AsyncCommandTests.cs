using LabelFlowStudio.Desktop.Commands;
using LabelFlowStudio.Application.Tests.Infrastructure;

namespace LabelFlowStudio.Application.Tests.Desktop.Commands;

public sealed class AsyncCommandTests
{
    [Fact]
    public void Constructor_Throws_WhenExecuteIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new AsyncCommand(null!, () => true, _ => { }));
    }

    [Fact]
    public async Task Execute_DisablesCommandWhileRunning_AndRaisesCanExecuteChanged()
    {
        var started = new TaskCompletionSource();
        var finish = new TaskCompletionSource();

        var changeCount = 0;

        var command = new AsyncCommand(
            async () =>
            {
                started.SetResult();
                await finish.Task;
            },
            () => true,
            _ => { });

        command.CanExecuteChanged += (_, _) => changeCount++;

        Assert.True(command.CanExecute(null));

        command.Execute(null);

        await started.Task;

        Assert.False(command.CanExecute(null));
        Assert.True(changeCount >= 1);

        finish.SetResult();

        await WaitHelpers.WaitUntilAsync(() => command.CanExecute(null), TimeSpan.FromSeconds(2));

        Assert.True(changeCount >= 2);
    }

    [Fact]
    public void Execute_DoesNotRun_WhenCanExecuteIsFalse()
    {
        var executed = false;

        var command = new AsyncCommand(
            () =>
            {
                executed = true;
                return Task.CompletedTask;
            },
            () => false,
            _ => { });

        Assert.False(command.CanExecute(null));

        command.Execute(null);

        Assert.False(executed);
    }
}
