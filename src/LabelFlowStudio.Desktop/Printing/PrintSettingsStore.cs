using System.IO;
using System.Text.Json;

namespace LabelFlowStudio.Desktop.Printing;

public static class PrintSettingsStore
{
    private static readonly object Gate = new();
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
                return _cached;
            }

            if (!File.Exists(SettingsFilePath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<PrintSettings>(json, JsonOptions);
                _cached = settings;
                return settings;
            }
            catch
            {
                return null;
            }
        }
    }

    public static PrintSettings LoadOrDefault()
    {
        return TryLoad() ?? new PrintSettings();
    }

    public static async Task SaveAsync(PrintSettings settings, CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(SettingsFilePath, json, cancellationToken);

        lock (Gate)
        {
            _cached = settings;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
