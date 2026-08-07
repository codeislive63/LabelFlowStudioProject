using System.IO;

namespace LabelFlowStudio.Desktop.Printing;

/// <summary>
/// Path-injectable repository for installation-specific storage and isolated tests.
/// </summary>
public sealed class PrintSettingsRepository : IPrintSettingsRepository
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly string _settingsFilePath;
    private readonly PrintSettings _defaults;
    private PrintSettings? _cached;

    public PrintSettingsRepository(string settingsFilePath, PrintSettings? defaults = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);

        _settingsFilePath = Path.GetFullPath(settingsFilePath);
        _defaults = (defaults ?? new PrintSettings()).Clone();
    }

    public string SettingsFilePath => _settingsFilePath;

    public PrintSettings? TryLoad()
    {
        lock (_gate)
        {
            if (_cached is not null)
            {
                return _cached.Clone();
            }

            if (!File.Exists(_settingsFilePath))
            {
                return null;
            }

            try
            {
                var settings = PrintSettingsFile.Read(_settingsFilePath);
                _cached = settings?.Clone();
                return settings?.Clone();
            }
            catch
            {
                return null;
            }
        }
    }

    public PrintSettings LoadOrDefault()
    {
        var settings = TryLoad() ?? _defaults.Clone();

        if (settings.EndLabelCopies <= 0)
        {
            settings.EndLabelCopies = _defaults.EndLabelCopies > 0 ? _defaults.EndLabelCopies : 2;
        }

        if (settings.StuffingSheetCopies <= 0)
        {
            settings.StuffingSheetCopies = _defaults.StuffingSheetCopies > 0
                ? _defaults.StuffingSheetCopies
                : 1;
        }

        return settings;
    }

    public async Task SaveAsync(PrintSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var snapshot = settings.Clone();
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PrintSettingsFile.WriteAtomicAsync(_settingsFilePath, snapshot, cancellationToken)
                .ConfigureAwait(false);

            lock (_gate)
            {
                _cached = snapshot.Clone();
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public PrintSettings Update(
        Func<PrintSettings, PrintSettings> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        _saveGate.Wait(cancellationToken);
        try
        {
            var latest = LoadOrDefault();
            var updated = update(latest.Clone())
                ?? throw new InvalidOperationException("Обновление настроек вернуло пустой результат.");
            var snapshot = updated.Clone();

            PrintSettingsFile.WriteAtomic(_settingsFilePath, snapshot, cancellationToken);

            lock (_gate)
            {
                _cached = snapshot.Clone();
            }

            return snapshot.Clone();
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task<PrintSettings> UpdateAsync(
        Func<PrintSettings, PrintSettings> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var latest = LoadOrDefault();
            var updated = update(latest.Clone())
                ?? throw new InvalidOperationException("Обновление настроек вернуло пустой результат.");
            var snapshot = updated.Clone();

            await PrintSettingsFile.WriteAtomicAsync(_settingsFilePath, snapshot, cancellationToken)
                .ConfigureAwait(false);

            lock (_gate)
            {
                _cached = snapshot.Clone();
            }

            return snapshot.Clone();
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
