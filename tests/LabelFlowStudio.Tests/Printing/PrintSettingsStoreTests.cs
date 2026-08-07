using System.Text.Json;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Desktop.Printing;

namespace LabelFlowStudio.Application.Tests.Printing;

public sealed class PrintSettingsStoreTests
{
    [Fact]
    public void TryLoad_WhenSettingsFileMissing_ReturnsNull()
    {
        using var scope = new TemporarySettingsRepository();

        var loaded = scope.Repository.TryLoad();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAsync_ThenTryLoad_RoundTripsEveryPersistedField()
    {
        using var scope = new TemporarySettingsRepository();
        var settings = CreateDistinctSettings();

        await scope.Repository.SaveAsync(settings, CancellationToken.None);

        var reloadedRepository = new PrintSettingsRepository(scope.SettingsFilePath);
        var loaded = reloadedRepository.TryLoad();

        Assert.NotNull(loaded);
        AssertSettingsEqual(settings, loaded);
    }

    [Fact]
    public async Task SaveAsync_UsesIndependentSnapshotsForInputAndCachedResults()
    {
        using var scope = new TemporarySettingsRepository();
        var settings = CreateDistinctSettings();

        await scope.Repository.SaveAsync(settings, CancellationToken.None);
        settings.EndLabelPrinterName = "Mutated caller";

        var firstRead = scope.Repository.LoadOrDefault();
        firstRead.StuffingSheetPrinterName = "Mutated result";
        var secondRead = scope.Repository.LoadOrDefault();

        Assert.Equal("End-label printer", firstRead.EndLabelPrinterName);
        Assert.Equal("Stuffing-sheet printer", secondRead.StuffingSheetPrinterName);
        Assert.NotSame(firstRead, secondRead);
    }

    [Fact]
    public async Task UpdateAsync_UsesLatestCachedSnapshotAndReturnsIndependentResult()
    {
        using var scope = new TemporarySettingsRepository();
        var initial = CreateDistinctSettings();
        await scope.Repository.SaveAsync(initial, CancellationToken.None);

        var updated = await scope.Repository.UpdateAsync(
            current =>
            {
                current.EndLabelCopies = 7;
                current.WorkMode = WorkMode.Manual;
                return current;
            },
            CancellationToken.None);
        updated.EndLabelCopies = 55;

        var loaded = scope.Repository.LoadOrDefault();
        Assert.Equal(7, loaded.EndLabelCopies);
        Assert.Equal(WorkMode.Manual, loaded.WorkMode);
        Assert.Equal(initial.ManualScanAutoPrintEndLabelEnabled, loaded.ManualScanAutoPrintEndLabelEnabled);
    }

    [Fact]
    public async Task Update_SynchronouslyReplacesFileAndCachedSnapshot()
    {
        using var scope = new TemporarySettingsRepository();
        var initial = CreateDistinctSettings();
        await scope.Repository.SaveAsync(initial, CancellationToken.None);

        var updated = scope.Repository.Update(
            current =>
            {
                current.EndLabelCopies = 8;
                return current;
            },
            CancellationToken.None);
        updated.EndLabelCopies = 55;

        var loaded = new PrintSettingsRepository(scope.SettingsFilePath).LoadOrDefault();
        Assert.Equal(8, loaded.EndLabelCopies);
        Assert.Equal(8, scope.Repository.LoadOrDefault().EndLabelCopies);
        Assert.Empty(Directory.EnumerateFiles(
            scope.DirectoryPath,
            $".{Path.GetFileName(scope.SettingsFilePath)}.*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_PreservesExistingJsonFieldNamesAndOmitsCalculatedState()
    {
        using var scope = new TemporarySettingsRepository();
        var settings = CreateDistinctSettings();

        await scope.Repository.SaveAsync(settings, CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(scope.SettingsFilePath));
        var properties = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "printEndLabelEnabled",
                "printStuffingSheetEnabled",
                "endLabelPrinterName",
                "stuffingSheetPrinterName",
                "endLabelCopies",
                "stuffingSheetCopies",
                "useScales",
                "manualScanAutoPrintEndLabelEnabled",
                "workMode"
            },
            properties);
        Assert.False(document.RootElement.TryGetProperty("isComplete", out _));
        Assert.Equal("End-label printer", document.RootElement.GetProperty("endLabelPrinterName").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("endLabelCopies").GetInt32());
    }

    [Fact]
    public async Task SaveAsync_LeavesNoAtomicWriteTemporaryFiles()
    {
        using var scope = new TemporarySettingsRepository();

        await scope.Repository.SaveAsync(CreateDistinctSettings(), CancellationToken.None);

        var leftovers = Directory.EnumerateFiles(
                scope.DirectoryPath,
                $".{Path.GetFileName(scope.SettingsFilePath)}.*.tmp")
            .ToArray();
        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task UpdateAsync_WhenAtomicReplaceFails_KeepsCacheAndCleansTemporaryFile()
    {
        using var scope = new TemporarySettingsRepository();
        var initial = CreateDistinctSettings();
        await scope.Repository.SaveAsync(initial, CancellationToken.None);

        File.Delete(scope.SettingsFilePath);
        Directory.CreateDirectory(scope.SettingsFilePath);

        var exception = await Record.ExceptionAsync(
            () => scope.Repository.UpdateAsync(
                current =>
                {
                    current.EndLabelCopies = 88;
                    return current;
                },
                CancellationToken.None));

        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        Assert.Equal(initial.EndLabelCopies, scope.Repository.LoadOrDefault().EndLabelCopies);
        Assert.Empty(Directory.EnumerateFiles(
            scope.DirectoryPath,
            $".{Path.GetFileName(scope.SettingsFilePath)}.*.tmp"));
    }

    [Fact]
    public async Task Update_WhenAtomicReplaceFails_KeepsCacheAndCleansTemporaryFile()
    {
        using var scope = new TemporarySettingsRepository();
        var initial = CreateDistinctSettings();
        await scope.Repository.SaveAsync(initial, CancellationToken.None);

        File.Delete(scope.SettingsFilePath);
        Directory.CreateDirectory(scope.SettingsFilePath);

        var exception = Record.Exception(
            () => scope.Repository.Update(
                current =>
                {
                    current.EndLabelCopies = 88;
                    return current;
                },
                CancellationToken.None));

        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        Assert.Equal(initial.EndLabelCopies, scope.Repository.LoadOrDefault().EndLabelCopies);
        Assert.Empty(Directory.EnumerateFiles(
            scope.DirectoryPath,
            $".{Path.GetFileName(scope.SettingsFilePath)}.*.tmp"));
    }

    [Fact]
    public void TryLoad_WhenJsonIsInvalid_ReturnsNull()
    {
        using var scope = new TemporarySettingsRepository("not json");

        var loaded = scope.Repository.TryLoad();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAsync_WhenSettingsIsNull_ThrowsArgumentNullException()
    {
        using var scope = new TemporarySettingsRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => scope.Repository.SaveAsync(settings: null!, CancellationToken.None));
    }

    [Fact]
    public void LoadOrDefault_WhenSettingsAreAbsent_ReturnsIndependentDefaults()
    {
        using var scope = new TemporarySettingsRepository();

        var first = scope.Repository.LoadOrDefault();
        first.EndLabelCopies = 77;
        var second = scope.Repository.LoadOrDefault();

        Assert.True(second.PrintEndLabelEnabled);
        Assert.True(second.PrintStuffingSheetEnabled);
        Assert.Equal(2, second.EndLabelCopies);
        Assert.Equal(1, second.StuffingSheetCopies);
        Assert.True(second.UseScales);
        Assert.False(second.ManualScanAutoPrintEndLabelEnabled);
        Assert.NotSame(first, second);
    }

    private static PrintSettings CreateDistinctSettings() => new()
    {
        EndLabelPrinterName = "End-label printer",
        StuffingSheetPrinterName = "Stuffing-sheet printer",
        EndLabelCopies = 3,
        StuffingSheetCopies = 4,
        PrintStuffingSheetEnabled = false,
        PrintEndLabelEnabled = true,
        UseScales = false,
        ManualScanAutoPrintEndLabelEnabled = true,
        WorkMode = WorkMode.Automatic
    };

    private static void AssertSettingsEqual(PrintSettings expected, PrintSettings actual)
    {
        Assert.Equal(expected.PrintEndLabelEnabled, actual.PrintEndLabelEnabled);
        Assert.Equal(expected.PrintStuffingSheetEnabled, actual.PrintStuffingSheetEnabled);
        Assert.Equal(expected.EndLabelPrinterName, actual.EndLabelPrinterName);
        Assert.Equal(expected.StuffingSheetPrinterName, actual.StuffingSheetPrinterName);
        Assert.Equal(expected.EndLabelCopies, actual.EndLabelCopies);
        Assert.Equal(expected.StuffingSheetCopies, actual.StuffingSheetCopies);
        Assert.Equal(expected.UseScales, actual.UseScales);
        Assert.Equal(
            expected.ManualScanAutoPrintEndLabelEnabled,
            actual.ManualScanAutoPrintEndLabelEnabled);
        Assert.Equal(expected.WorkMode, actual.WorkMode);
    }

    private sealed class TemporarySettingsRepository : IDisposable
    {
        public TemporarySettingsRepository(string? initialContent = null)
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "LabelFlowStudio.Tests",
                Guid.NewGuid().ToString("N"));
            SettingsFilePath = Path.Combine(DirectoryPath, "print-settings.json");
            Repository = new PrintSettingsRepository(SettingsFilePath);

            if (initialContent is not null)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(SettingsFilePath, initialContent);
            }
        }

        public string DirectoryPath { get; }

        public string SettingsFilePath { get; }

        public PrintSettingsRepository Repository { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
