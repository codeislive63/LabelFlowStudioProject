using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.Tests.BoxProcessing;

public sealed class BoxProcessingServiceTests
{
    private sealed class FakeLabelRepository : ILabelRepository
    {
        private readonly IReadOnlyList<LabelRecord> _records;

        public FakeLabelRepository(IReadOnlyList<LabelRecord> records)
        {
            _records = records;
        }

        public string LastTenam { get; private set; } = string.Empty;

        public Task<IReadOnlyList<LabelRecord>> GetByTenamAsync(string tenam, CancellationToken cancellationToken)
        {
            LastTenam = tenam;
            return Task.FromResult(_records);
        }
    }

    [Fact]
    public async Task ProcessAsync_RequestIsNull_ThrowsArgumentNullException()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());
        var service = new BoxProcessingService(repository);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await service.ProcessAsync(null!, CancellationToken.None);
        });
    }

    [Fact]
    public async Task ProcessAsync_TenamIsWhitespace_ReturnsError()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());
        var service = new BoxProcessingService(repository);

        var response = await service.ProcessAsync(
            new BoxProcessingRequest("   ", WorkMode.Manual, ShouldPrintEndLabels: true, ShouldPrintStuffingSheet: true),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.Error, response.Status);
        Assert.Equal("TENAM пустой", response.Message);
        Assert.Empty(response.Records);
        Assert.False(response.ShouldPrintDropSheet);
        Assert.False(response.ShouldPrintEmptyDropSheet);
        Assert.False(response.ShouldPrintEndLabels);
    }

    [Fact]
    public async Task ProcessAsync_TrimsTenam_BeforeQuery()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());
        var service = new BoxProcessingService(repository);

        var response = await service.ProcessAsync(
            new BoxProcessingRequest(" 4340558 ", WorkMode.Manual, ShouldPrintEndLabels: false, ShouldPrintStuffingSheet: true),
            CancellationToken.None
        );

        Assert.Equal("4340558", repository.LastTenam);
        Assert.Equal(BoxProcessingStatus.NotFound, response.Status);
    }

    [Fact]
    public async Task ProcessAsync_NoRecords_ReturnsNotFound()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());
        var service = new BoxProcessingService(repository);

        var response = await service.ProcessAsync(
            new BoxProcessingRequest("4340558", WorkMode.Manual, ShouldPrintEndLabels: true, ShouldPrintStuffingSheet: true),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.NotFound, response.Status);
        Assert.Equal("Данных по коробу не найдено", response.Message);
        Assert.Empty(response.Records);
        Assert.False(response.ShouldPrintDropSheet);
        Assert.False(response.ShouldPrintEmptyDropSheet);
        Assert.False(response.ShouldPrintEndLabels);
    }

    [Fact]
    public async Task ProcessAsync_WeightMissing_Manual_ReturnsNeedWeight()
    {
        var records = new[]
        {
            CreateRecord(brutto: null)
        };

        var repository = new FakeLabelRepository(records);
        var service = new BoxProcessingService(repository);

        var response = await service.ProcessAsync(
            new BoxProcessingRequest("4340558", WorkMode.Manual, ShouldPrintEndLabels: true, ShouldPrintStuffingSheet: true),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.NeedWeight, response.Status);
        Assert.Equal("Нет веса в БД. Поставьте короб на весы", response.Message);
        Assert.Single(response.Records);
        Assert.False(response.ShouldPrintDropSheet);
        Assert.False(response.ShouldPrintEmptyDropSheet);
        Assert.False(response.ShouldPrintEndLabels);
    }

    [Fact]
    public async Task ProcessAsync_WeightMissing_Automatic_PrintsEmptyDropSheet()
    {
        var records = new[]
        {
            CreateRecord(brutto: 0m)
        };

        var repository = new FakeLabelRepository(records);
        var service = new BoxProcessingService(repository);

        var response = await service.ProcessAsync(
            new BoxProcessingRequest("4340558", WorkMode.Automatic, ShouldPrintEndLabels: true, ShouldPrintStuffingSheet: true),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.Success, response.Status);
        Assert.Equal("Нет веса в БД. Авто-режим: печатаю пустой лист сброса", response.Message);
        Assert.Single(response.Records);
        Assert.False(response.ShouldPrintDropSheet);
        Assert.True(response.ShouldPrintEmptyDropSheet);
        Assert.True(response.ShouldPrintEndLabels);
    }

    [Fact]
    public async Task ProcessAsync_WeightPresent_ReturnsSuccessAndPrintsDropSheet()
    {
        var records = new[]
        {
            CreateRecord(brutto: 6.325m)
        };

        var repository = new FakeLabelRepository(records);
        var service = new BoxProcessingService(repository);

        var response = await service.ProcessAsync(
            new BoxProcessingRequest("4340558", WorkMode.Manual, ShouldPrintEndLabels: false, ShouldPrintStuffingSheet: true),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.Success, response.Status);
        Assert.Equal("Данные загружены", response.Message);
        Assert.Single(response.Records);
        Assert.Equal(6.325m, response.Weight);
        Assert.True(response.ShouldPrintDropSheet);
        Assert.False(response.ShouldPrintEmptyDropSheet);
        Assert.False(response.ShouldPrintEndLabels);
    }


    [Fact]
    public async Task ProcessAsync_WeightPresent_StuffingSheetDisabled_DoesNotRequestDropSheet()
    {
        var records = new[]
        {
            CreateRecord(brutto: 5m)
        };

        var repository = new FakeLabelRepository(records);
        var service = new BoxProcessingService(repository);

        var response = await service.ProcessAsync(
            new BoxProcessingRequest("4340558", WorkMode.Manual, ShouldPrintEndLabels: true, ShouldPrintStuffingSheet: false),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.Success, response.Status);
        Assert.False(response.ShouldPrintDropSheet);
        Assert.False(response.ShouldPrintEmptyDropSheet);
        Assert.True(response.ShouldPrintEndLabels);
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
