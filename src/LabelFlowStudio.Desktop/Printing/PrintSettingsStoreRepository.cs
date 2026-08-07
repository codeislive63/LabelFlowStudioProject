namespace LabelFlowStudio.Desktop.Printing;

/// <summary>
/// Production adapter that keeps the existing static store as the single source
/// used by the current printing pipeline.
/// </summary>
public sealed class PrintSettingsStoreRepository : IPrintSettingsRepository
{
    public PrintSettings? TryLoad() => PrintSettingsStore.TryLoad();

    public PrintSettings LoadOrDefault() => PrintSettingsStore.LoadOrDefault();

    public Task SaveAsync(PrintSettings settings, CancellationToken cancellationToken) =>
        PrintSettingsStore.SaveAsync(settings, cancellationToken);

    public PrintSettings Update(
        Func<PrintSettings, PrintSettings> update,
        CancellationToken cancellationToken) =>
        PrintSettingsStore.Update(update, cancellationToken);

    public Task<PrintSettings> UpdateAsync(
        Func<PrintSettings, PrintSettings> update,
        CancellationToken cancellationToken) =>
        PrintSettingsStore.UpdateAsync(update, cancellationToken);
}
