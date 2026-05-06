using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Tests.BoxProcessing;

public sealed class BoxWeightServiceTests
{
    [Fact]
    public void Constructor_RepositoryIsNull_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new BoxWeightService(null!));

        Assert.Equal("labelRepository", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UpdateWeightAsync_TenamIsEmpty_ReturnsFailureAndDoesNotCallRepository(string tenam)
    {
        var repository = new FakeLabelRepository();
        var service = new BoxWeightService(repository);

        var result = await service.UpdateWeightAsync(tenam, 5m, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("TENAM пустой", result.Message);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UpdateWeightAsync_WeightIsNotPositive_ReturnsFailureAndDoesNotCallRepository(decimal weight)
    {
        var repository = new FakeLabelRepository();
        var service = new BoxWeightService(repository);

        var result = await service.UpdateWeightAsync("4340558", weight, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Вес должен быть больше нуля", result.Message);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public async Task UpdateWeightAsync_ValidInput_TrimsTenamAndDelegatesToRepository()
    {
        var repository = new FakeLabelRepository
        {
            UpdateResult = true
        };

        var service = new BoxWeightService(repository);

        var result = await service.UpdateWeightAsync(" 4340558 ", 5.125m, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Вес сохранен в БД", result.Message);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal("4340558", repository.LastUpdatedTenam);
        Assert.Equal(5.125m, repository.LastUpdatedWeight);
    }

    [Fact]
    public async Task UpdateWeightAsync_RepositoryReturnsFalse_ReturnsFailure()
    {
        var repository = new FakeLabelRepository
        {
            UpdateResult = false
        };

        var service = new BoxWeightService(repository);

        var result = await service.UpdateWeightAsync("4340558", 5.125m, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Не удалось сохранить вес в БД", result.Message);
        Assert.Equal(1, repository.UpdateCalls);
    }

    private sealed class FakeLabelRepository : ILabelRepository
    {
        public bool UpdateResult { get; init; } = true;

        public int UpdateCalls { get; private set; }

        public string LastUpdatedTenam { get; private set; } = string.Empty;

        public decimal LastUpdatedWeight { get; private set; }

        public Task<IReadOnlyList<LabelRecord>> GetByTenamAsync(
            string tenam,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LabelRecord>>([]);
        }

        public Task<bool> UpdateBruttoByTenamAsync(
            string tenam,
            decimal brutto,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            LastUpdatedTenam = tenam;
            LastUpdatedWeight = brutto;

            return Task.FromResult(UpdateResult);
        }
    }
}
