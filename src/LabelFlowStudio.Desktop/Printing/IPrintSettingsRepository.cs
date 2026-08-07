namespace LabelFlowStudio.Desktop.Printing;

/// <summary>
/// Provides independent snapshots of the active print configuration.
/// </summary>
public interface IPrintSettingsRepository
{
    PrintSettings? TryLoad();

    PrintSettings LoadOrDefault();

    Task SaveAsync(PrintSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a small settings update without yielding the calling UI thread.
    /// This closes the gap in which a new box could start between the busy check
    /// and replacement of the active print-settings snapshot.
    /// </summary>
    PrintSettings Update(
        Func<PrintSettings, PrintSettings> update,
        CancellationToken cancellationToken);

    Task<PrintSettings> UpdateAsync(
        Func<PrintSettings, PrintSettings> update,
        CancellationToken cancellationToken);
}
