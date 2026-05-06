using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Policies;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Tests.BoxProcessing;

public sealed class BoxProcessingServiceTests
{
    private sealed class FakeLabelRepository(IReadOnlyList<LabelRecord> records) : ILabelRepository
    {
        private readonly IReadOnlyList<LabelRecord> _records = records;

        public string LastTenam { get; private set; } = string.Empty;

        public Task<IReadOnlyList<LabelRecord>> GetByTenamAsync(
            string tenam,
            CancellationToken cancellationToken)
        {
            LastTenam = tenam;
            return Task.FromResult(_records);
        }

        public Task<bool> UpdateBruttoByTenamAsync(
            string tenam,
            decimal brutto,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task ProcessAsync_RequestIsNull_ThrowsArgumentNullException()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());
        var service = CreateService(repository);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await service.ProcessAsync(null!, CancellationToken.None);
        });
    }

    [Fact]
    public async Task ProcessAsync_TenamIsWhitespace_ReturnsError()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());
        var service = CreateService(repository);

        var response = await service.ProcessAsync(
            CreateRequest("   "),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.Error, response.Status);
        Assert.Equal("TENAM пустой", response.Message);
        Assert.Empty(response.Records);
        Assert.Equal(PrintPlan.None, response.PrintPlan);
    }

    [Fact]
    public async Task ProcessAsync_TrimsTenam_BeforeQuery()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());
        var service = CreateService(repository);

        var response = await service.ProcessAsync(
            CreateRequest(" 4340558 "),
            CancellationToken.None
        );

        Assert.Equal("4340558", repository.LastTenam);
        Assert.Equal(BoxProcessingStatus.NotFound, response.Status);
    }

    [Fact]
    public async Task ProcessAsync_NoRecords_ReturnsNotFound()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());
        var service = CreateService(repository);

        var response = await service.ProcessAsync(
            CreateRequest("4340558"),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.NotFound, response.Status);
        Assert.Equal("Данных по коробу не найдено", response.Message);
        Assert.Empty(response.Records);
        Assert.Equal(PrintPlan.None, response.PrintPlan);
    }

    [Fact]
    public async Task ProcessAsync_WeightMissing_Manual_ReturnsNeedWeightWithoutPrintPlan()
    {
        var records = new[]
        {
            CreateRecord(brutto: null)
        };

        var repository = new FakeLabelRepository(records);
        var service = CreateService(repository);

        var response = await service.ProcessAsync(
            CreateRequest("4340558", WorkMode.Manual),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.NeedWeight, response.Status);
        Assert.Equal("Нет веса в БД. Поставьте короб на весы", response.Message);
        Assert.Single(response.Records);
        Assert.Equal(PrintPlan.None, response.PrintPlan);
    }

    [Fact]
    public async Task ProcessAsync_WeightMissing_AutomaticWithoutScales_ReturnsNeedWeightWithEmptyDropSheetPlan()
    {
        var records = new[]
        {
            CreateRecord(brutto: 0m)
        };

        var repository = new FakeLabelRepository(records);
        var service = CreateService(repository);

        var response = await service.ProcessAsync(
            CreateRequest(
                tenam: "4340558",
                mode: WorkMode.Automatic,
                shouldPrintEndLabels: true,
                shouldPrintStuffingSheet: true,
                useScales: false),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.NeedWeight, response.Status);
        Assert.Equal("Нет веса в БД. Поставьте короб на весы", response.Message);
        Assert.Single(response.Records);
        Assert.Equal(
            new PrintPlan(
                PrintDropSheet: false,
                PrintEmptyDropSheet: true,
                PrintEndLabels: false),
            response.PrintPlan);
    }

    [Fact]
    public async Task ProcessAsync_WeightMissing_AutomaticWithScales_ReturnsNeedWeightWithoutPrintPlan()
    {
        var records = new[]
        {
            CreateRecord(brutto: 0m)
        };

        var repository = new FakeLabelRepository(records);
        var service = CreateService(repository);

        var response = await service.ProcessAsync(
            CreateRequest(
                tenam: "4340558",
                mode: WorkMode.Automatic,
                shouldPrintEndLabels: true,
                shouldPrintStuffingSheet: true,
                useScales: true),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.NeedWeight, response.Status);
        Assert.Equal(PrintPlan.None, response.PrintPlan);
    }

    [Fact]
    public async Task ProcessAsync_WeightPresent_ReturnsSuccessAndPrintPlan()
    {
        var records = new[]
        {
            CreateRecord(brutto: 6.325m)
        };

        var repository = new FakeLabelRepository(records);
        var service = CreateService(repository);

        var response = await service.ProcessAsync(
            CreateRequest(
                tenam: "4340558",
                mode: WorkMode.Manual,
                shouldPrintEndLabels: false,
                shouldPrintStuffingSheet: true),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.Success, response.Status);
        Assert.Equal("Данные загружены", response.Message);
        Assert.Single(response.Records);
        Assert.Equal(6.325m, response.Weight);
        Assert.Equal(
            new PrintPlan(
                PrintDropSheet: true,
                PrintEmptyDropSheet: false,
                PrintEndLabels: false),
            response.PrintPlan);
    }

    [Fact]
    public async Task ProcessAsync_WeightPresent_StuffingSheetDisabled_DoesNotRequestDropSheet()
    {
        var records = new[]
        {
            CreateRecord(brutto: 5m)
        };

        var repository = new FakeLabelRepository(records);
        var service = CreateService(repository);

        var response = await service.ProcessAsync(
            CreateRequest(
                tenam: "4340558",
                mode: WorkMode.Manual,
                shouldPrintEndLabels: true,
                shouldPrintStuffingSheet: false),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.Success, response.Status);
        Assert.Equal(
            new PrintPlan(
                PrintDropSheet: false,
                PrintEmptyDropSheet: false,
                PrintEndLabels: true),
            response.PrintPlan);
    }

    [Fact]
    public async Task ProcessAsync_WeightConflict_ReturnsErrorAndDoesNotPrint()
    {
        var records = new[]
        {
            CreateRecord(brutto: 5m),
            CreateRecord(brutto: 7m)
        };

        var repository = new FakeLabelRepository(records);
        var service = CreateService(repository);

        var response = await service.ProcessAsync(
            CreateRequest("4340558"),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.Error, response.Status);
        Assert.Equal("В БД найдено несколько разных значений веса для одного короба", response.Message);
        Assert.Equal(PrintPlan.None, response.PrintPlan);
    }

    [Fact]
    public async Task ProcessAsync_TenamIsNull_ReturnsError()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());
        var service = CreateService(repository);

        var response = await service.ProcessAsync(
            CreateRequest(null!),
            CancellationToken.None
        );

        Assert.Equal(BoxProcessingStatus.Error, response.Status);
        Assert.Equal("TENAM пустой", response.Message);
        Assert.Empty(response.Records);
    }

    [Fact]
    public void Constructor_RepositoryIsNull_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new BoxProcessingService(
                null!,
                new BoxWeightResolver(),
                new BoxProcessingPolicy()));

        Assert.Equal("labelRepository", exception.ParamName);
    }

    [Fact]
    public void Constructor_WeightResolverIsNull_ThrowsArgumentNullException()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new BoxProcessingService(
                repository,
                null!,
                new BoxProcessingPolicy()));

        Assert.Equal("weightResolver", exception.ParamName);
    }

    [Fact]
    public void Constructor_ProcessingPolicyIsNull_ThrowsArgumentNullException()
    {
        var repository = new FakeLabelRepository(Array.Empty<LabelRecord>());

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new BoxProcessingService(
                repository,
                new BoxWeightResolver(),
                null!));

        Assert.Equal("processingPolicy", exception.ParamName);
    }

    private static BoxProcessingService CreateService(ILabelRepository repository)
    {
        return new BoxProcessingService(
            repository,
            new BoxWeightResolver(),
            new BoxProcessingPolicy());
    }

    private static BoxProcessingRequest CreateRequest(
        string tenam,
        WorkMode mode = WorkMode.Manual,
        bool shouldPrintEndLabels = true,
        bool shouldPrintStuffingSheet = true,
        bool useScales = false)
    {
        return new BoxProcessingRequest(
            Tenam: tenam,
            Mode: mode,
            ShouldPrintEndLabels: shouldPrintEndLabels,
            ShouldPrintStuffingSheet: shouldPrintStuffingSheet,
            UseScales: useScales
        );
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
