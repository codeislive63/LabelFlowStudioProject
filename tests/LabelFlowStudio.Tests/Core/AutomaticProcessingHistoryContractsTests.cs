using LabelFlowStudio.Core.Statistics;

namespace LabelFlowStudio.Application.Tests.Core;

public sealed class AutomaticProcessingHistoryContractsTests
{
    [Fact]
    public void Outcomes_HaveStablePersistenceValues()
    {
        Assert.Equal(0, (int)AutomaticProcessingOutcome.Success);
        Assert.Equal(1, (int)AutomaticProcessingOutcome.Warning);
        Assert.Equal(2, (int)AutomaticProcessingOutcome.Error);
    }

    [Fact]
    public void Aggregate_PreservesCountsAndUtcBoundaries()
    {
        var first = new DateTimeOffset(2026, 8, 10, 5, 10, 0, TimeSpan.Zero);
        var last = first.AddMinutes(15);

        var aggregate = new AutomaticProcessingHistoryAggregate(7, 5, 1, first, last);

        Assert.Equal(7, aggregate.CompletedCount);
        Assert.Equal(5, aggregate.SuccessCount);
        Assert.Equal(1, aggregate.ErrorCount);
        Assert.Equal(first, aggregate.FirstCompletedAtUtc);
        Assert.Equal(last, aggregate.LastCompletedAtUtc);
    }
}
