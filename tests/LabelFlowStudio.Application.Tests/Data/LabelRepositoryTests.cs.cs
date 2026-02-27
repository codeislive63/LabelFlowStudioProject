using LabelFlowStudio.Data.Oracle;
using LabelFlowStudio.Data.Oracle.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LabelFlowStudio.Application.Tests.Data;

public sealed class LabelRepositoryTests
{
    [Fact]
    public async Task GetByTenamAsync_Throws_WhenTenamEmpty()
    {
        await using var context = CreateContext(nameof(GetByTenamAsync_Throws_WhenTenamEmpty));
        var repository = new LabelRepository(new TestDbContextFactory(context));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.GetByTenamAsync("  ", CancellationToken.None));
    }

    [Fact]
    public async Task GetByTenamAsync_TrimsInput_AndReturnsEmptyWhenNoRows()
    {
        await using var context = CreateContext(nameof(GetByTenamAsync_TrimsInput_AndReturnsEmptyWhenNoRows));
        var repository = new LabelRepository(new TestDbContextFactory(context));

        var result = await repository.GetByTenamAsync(" 4340558 ", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    private static LabelDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<LabelDbContext>()
            .UseInMemoryDatabase($"LabelRepositoryTests_{dbName}_{Guid.NewGuid()}")
            .Options;
        return new LabelDbContext(options);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<LabelDbContext>
    {
        private readonly LabelDbContext _context;

        public TestDbContextFactory(LabelDbContext context)
        {
            _context = context;
        }

        public LabelDbContext CreateDbContext() => _context;

        public Task<LabelDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_context);
    }
}