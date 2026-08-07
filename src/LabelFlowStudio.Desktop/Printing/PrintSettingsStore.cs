using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;

namespace LabelFlowStudio.Desktop.Printing;

public static class PrintSettingsStore
{
    private static readonly object Gate = new();
    private static readonly SemaphoreSlim SaveGate = new(1, 1);
    private static PrintSettings? _cached;

    public static string SettingsFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LabelFlowStudio",
        "print-settings.json");

    public static PrintSettings? TryLoad()
    {
        lock (Gate)
        {
            if (_cached is not null)
            {
                return _cached.Clone();
            }

            if (!File.Exists(SettingsFilePath))
            {
                return null;
            }

            try
            {
                var settings = PrintSettingsFile.Read(SettingsFilePath);
                _cached = settings?.Clone();
                return settings?.Clone();
            }
            catch
            {
                return null;
            }
        }
    }

    public static PrintSettings LoadOrDefault()
    {
        var loadedSettings = TryLoad();
        var settings = loadedSettings ?? new PrintSettings();
        var defaults = LoadConfiguredDefaults();

        if (settings.EndLabelCopies <= 0)
        {
            settings.EndLabelCopies = defaults.EndLabelCopies > 0 ? defaults.EndLabelCopies : 2;
        }

        if (settings.StuffingSheetCopies <= 0)
        {
            settings.StuffingSheetCopies = defaults.StuffingSheetCopies > 0 ? defaults.StuffingSheetCopies : 1;
        }

        if (loadedSettings is null)
        {
            settings.ManualScanAutoPrintEndLabelEnabled = defaults.ManualScanAutoPrintEndLabelEnabled;
        }

        return settings;
    }

    public static async Task SaveAsync(PrintSettings settings, CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var snapshot = settings.Clone();
        await SaveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PrintSettingsFile.WriteAtomicAsync(SettingsFilePath, snapshot, cancellationToken)
                .ConfigureAwait(false);

            lock (Gate)
            {
                _cached = snapshot.Clone();
            }
        }
        finally
        {
            SaveGate.Release();
        }
    }

    /// <summary>
    /// Applies the settings snapshot atomically without yielding the caller.
    /// The Settings UI uses this tiny synchronous critical section so scanner
    /// callbacks cannot begin a box between its final busy check and cache swap.
    /// </summary>
    public static PrintSettings Update(
        Func<PrintSettings, PrintSettings> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        SaveGate.Wait(cancellationToken);
        try
        {
            var latest = LoadOrDefault();
            var updated = update(latest.Clone())
                ?? throw new InvalidOperationException("Обновление настроек вернуло пустой результат.");
            var snapshot = updated.Clone();

            PrintSettingsFile.WriteAtomic(SettingsFilePath, snapshot, cancellationToken);

            lock (Gate)
            {
                _cached = snapshot.Clone();
            }

            return snapshot.Clone();
        }
        finally
        {
            SaveGate.Release();
        }
    }

    /// <summary>
    /// Applies a focused patch to the latest snapshot while holding the same gate
    /// as full saves. Callers that own only part of the configuration should use
    /// this method so concurrent runtime updates do not overwrite one another.
    /// </summary>
    public static async Task<PrintSettings> UpdateAsync(
        Func<PrintSettings, PrintSettings> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        await SaveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var latest = LoadOrDefault();
            var updated = update(latest.Clone())
                ?? throw new InvalidOperationException("Обновление настроек вернуло пустой результат.");
            var snapshot = updated.Clone();

            await PrintSettingsFile.WriteAtomicAsync(SettingsFilePath, snapshot, cancellationToken)
                .ConfigureAwait(false);

            lock (Gate)
            {
                _cached = snapshot.Clone();
            }

            return snapshot.Clone();
        }
        finally
        {
            SaveGate.Release();
        }
    }

    private static PrintSettingsDefaults LoadConfiguredDefaults()
    {
        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

            var defaults = new PrintSettingsDefaults();
            configuration.GetSection("PrintSettingsDefaults").Bind(defaults);
            return defaults;
        }
        catch
        {
            return new PrintSettingsDefaults();
        }
    }

    private sealed class PrintSettingsDefaults
    {
        public int EndLabelCopies { get; set; } = 2;
        public int StuffingSheetCopies { get; set; } = 1;
        public bool ManualScanAutoPrintEndLabelEnabled { get; set; }
    }
}

/// <summary>
/// Shared JSON persistence primitive. The temporary file is created beside the
/// target so the final rename remains on the same volume.
/// </summary>
internal static class PrintSettingsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static PrintSettings? Read(string settingsFilePath)
    {
        var json = File.ReadAllText(settingsFilePath);
        return JsonSerializer.Deserialize<PrintSettings>(json, JsonOptions);
    }

    public static async Task WriteAtomicAsync(
        string settingsFilePath,
        PrintSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        ArgumentNullException.ThrowIfNull(settings);

        var fullPath = Path.GetFullPath(settingsFilePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Не удалось определить каталог файла настроек печати.");

        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // A stale temp file is less harmful than masking the persistence error.
            }
        }
    }

    public static void WriteAtomic(
        string settingsFilePath,
        PrintSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(settingsFilePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Не удалось определить каталог файла настроек печати.");

        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // A stale temp file is less harmful than masking the persistence error.
            }
        }
    }
}
