using System.Reflection;
using LabelFlowStudio.Desktop.Printing;

namespace LabelFlowStudio.Application.Tests.Printing;

[Collection("PrintSettingsStore")]
public sealed class PrintSettingsStoreTests
{
    [Fact]
    public void TryLoad_WhenSettingsFileMissing_ReturnsNull()
    {
        using var _ = new SettingsFileScope("missing-file");

        var loaded = PrintSettingsStore.TryLoad();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAsync_ThenTryLoad_ReturnsSavedValues()
    {
        using var _ = new SettingsFileScope("save-then-load");

        var settings = new PrintSettings
        {
            EndLabelPrinterName = "Zebra",
            StuffingSheetPrinterName = "LaserJet",
            EndLabelCopies = 3,
            StuffingSheetCopies = 2,
            PrintStuffingSheetEnabled = true,
            PrintEndLabelEnabled = true,
            UseScales = false
        };

        await PrintSettingsStore.SaveAsync(settings, CancellationToken.None);

        ResetCache();
        var loaded = PrintSettingsStore.TryLoad();

        Assert.NotNull(loaded);
        Assert.Equal("Zebra", loaded.EndLabelPrinterName);
        Assert.Equal("LaserJet", loaded.StuffingSheetPrinterName);
        Assert.Equal(3, loaded.EndLabelCopies);
        Assert.Equal(2, loaded.StuffingSheetCopies);
        Assert.False(loaded.UseScales);
    }

    [Fact]
    public void TryLoad_WhenJsonIsInvalid_ReturnsNull()
    {
        using var _ = new SettingsFileScope("invalid-json", "not json");

        ResetCache();
        var loaded = PrintSettingsStore.TryLoad();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAsync_WhenSettingsIsNull_ThrowsArgumentNullException()
    {
        using var _ = new SettingsFileScope("null-argument");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => PrintSettingsStore.SaveAsync(settings: null!, CancellationToken.None)
        );
    }

    [Fact]
    public void LoadOrDefault_WhenSettingsAreAbsent_ReturnsDefaults()
    {
        using var _ = new SettingsFileScope("load-defaults");

        var settings = PrintSettingsStore.LoadOrDefault();

        Assert.True(settings.PrintEndLabelEnabled);
        Assert.True(settings.PrintStuffingSheetEnabled);
        Assert.Equal(2, settings.EndLabelCopies);
        Assert.Equal(1, settings.StuffingSheetCopies);
        Assert.True(settings.UseScales);
    }

    private static void ResetCache()
    {
        var t = typeof(PrintSettingsStore);

        var gateField = t.GetField("Gate", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Gate field not found");

        var cacheField = t.GetField("_cached", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("_cached field not found");

        var gate = gateField.GetValue(null)
            ?? throw new InvalidOperationException("Gate is null");

        lock (gate)
        {
            cacheField.SetValue(null, null);
        }
    }

    private sealed class SettingsFileScope : IDisposable
    {
        private readonly string _path;
        private readonly string? _backup;

        public SettingsFileScope(string _marker, string? initialContent = null)
        {
            _path = PrintSettingsStore.SettingsFilePath;
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);

            _backup = File.Exists(_path) ? File.ReadAllText(_path) : null;
            if (initialContent is null)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            else
            {
                File.WriteAllText(_path, initialContent);
            }

            ResetCache();
        }

        public void Dispose()
        {
            if (_backup is null)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            else
            {
                File.WriteAllText(_path, _backup);
            }

            ResetCache();
        }
    }
}

[CollectionDefinition("PrintSettingsStore", DisableParallelization = true)]
public sealed class PrintSettingsStoreCollection { }
