using Microsoft.Extensions.Configuration;

namespace LabelFlowStudio.Data.Statistics;

internal static class AutomaticProcessingDatabasePathResolver
{
    private const string DatabasePathKey = "LocalStorage:DatabasePath";
    private const string StorageScopeKey = "LocalStorage:Scope";

    internal static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return Resolve(configuration, Environment.GetFolderPath);
    }

    internal static string Resolve(
        IConfiguration configuration,
        Func<Environment.SpecialFolder, string> getFolderPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(getFolderPath);

        string? configuredPath = configuration[DatabasePathKey];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            if (!Path.IsPathFullyQualified(expandedPath))
            {
                throw new InvalidOperationException(
                    $"Configuration value '{DatabasePathKey}' must be an absolute path.");
            }

            return Path.GetFullPath(expandedPath);
        }

        string? configuredScope = configuration[StorageScopeKey];
        Environment.SpecialFolder specialFolder = configuredScope?.Trim() switch
        {
            null or "" => Environment.SpecialFolder.LocalApplicationData,
            string value when value.Equals("LocalAppData", StringComparison.OrdinalIgnoreCase) =>
                Environment.SpecialFolder.LocalApplicationData,
            string value when value.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) =>
                Environment.SpecialFolder.CommonApplicationData,
            _ => throw new InvalidOperationException(
                $"Configuration value '{StorageScopeKey}' must be either 'LocalAppData' or 'ProgramData'.")
        };

        string basePath = getFolderPath(specialFolder);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new InvalidOperationException(
                $"The operating system did not provide a path for storage scope '{configuredScope ?? "LocalAppData"}'.");
        }

        return Path.Combine(basePath, "LabelFlowStudio", "data", "LabelFlowStudio.db");
    }
}
