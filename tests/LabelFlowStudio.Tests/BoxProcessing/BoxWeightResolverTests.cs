using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Tests.BoxProcessing;

public sealed class BoxWeightResolverTests
{
    [Fact]
    public void Resolve_RecordsIsNull_ThrowsArgumentNullException()
    {
        var resolver = new BoxWeightResolver();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            resolver.Resolve(null!));

        Assert.Equal("records", exception.ParamName);
    }

    [Fact]
    public void Resolve_NoRecords_ReturnsMissing()
    {
        var resolver = new BoxWeightResolver();

        var result = resolver.Resolve([]);

        Assert.False(result.HasWeight);
        Assert.False(result.HasConflict);
        Assert.Null(result.Weight);
        Assert.Equal("Нет веса в БД. Поставьте короб на весы", result.Message);
    }

    [Fact]
    public void Resolve_NoPositiveWeights_ReturnsMissing()
    {
        var resolver = new BoxWeightResolver();

        var result = resolver.Resolve(
        [
            CreateRecord(null),
            CreateRecord(0m),
            CreateRecord(-1m)
        ]);

        Assert.False(result.HasWeight);
        Assert.False(result.HasConflict);
        Assert.Null(result.Weight);
        Assert.Equal("Нет веса в БД. Поставьте короб на весы", result.Message);
    }

    [Fact]
    public void Resolve_OnePositiveWeight_ReturnsFound()
    {
        var resolver = new BoxWeightResolver();

        var result = resolver.Resolve(
        [
            CreateRecord(6.325m)
        ]);

        Assert.True(result.HasWeight);
        Assert.False(result.HasConflict);
        Assert.Equal(6.325m, result.Weight);
        Assert.Equal(string.Empty, result.Message);
    }

    [Fact]
    public void Resolve_MultipleSamePositiveWeights_ReturnsFound()
    {
        var resolver = new BoxWeightResolver();

        var result = resolver.Resolve(
        [
            CreateRecord(6.325m),
            CreateRecord(6.325m),
            CreateRecord(null),
            CreateRecord(0m)
        ]);

        Assert.True(result.HasWeight);
        Assert.False(result.HasConflict);
        Assert.Equal(6.325m, result.Weight);
    }

    [Fact]
    public void Resolve_MultipleDifferentPositiveWeights_ReturnsConflict()
    {
        var resolver = new BoxWeightResolver();

        var result = resolver.Resolve(
        [
            CreateRecord(6.325m),
            CreateRecord(7.125m)
        ]);

        Assert.False(result.HasWeight);
        Assert.True(result.HasConflict);
        Assert.Null(result.Weight);
        Assert.Equal("В БД найдено несколько разных значений веса для одного короба", result.Message);
    }

    [Fact]
    public void Resolve_IgnoresInvalidWeights_WhenOneValidWeightExists()
    {
        var resolver = new BoxWeightResolver();

        var result = resolver.Resolve(
        [
            CreateRecord(null),
            CreateRecord(0m),
            CreateRecord(-2m),
            CreateRecord(4.5m)
        ]);

        Assert.True(result.HasWeight);
        Assert.False(result.HasConflict);
        Assert.Equal(4.5m, result.Weight);
    }

    private static LabelRecord CreateRecord(decimal? brutto)
    {
        return new LabelRecord
        {
            Tenam = "4340558",
            Brutto = brutto
        };
    }
}
