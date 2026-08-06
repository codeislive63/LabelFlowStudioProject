using Microsoft.Web.WebView2.Core;
using System.IO;

namespace LabelFlowStudio.Desktop.Printing;

/// <summary>
/// Creates the single WebView2 environment used by previews and silent printing.
/// </summary>
internal static class WebView2EnvironmentProvider
{
    private const string SoftwareRenderingArguments = "--disable-gpu";

    private static readonly object Sync = new();
    private static Task<CoreWebView2Environment>? _environmentTask;

    /// <summary>
    /// Returns a shared environment configured to render WebView2 content on the CPU.
    /// A faulted initialization is discarded so a later operation can retry.
    /// </summary>
    public static async Task<CoreWebView2Environment> GetAsync()
    {
        Task<CoreWebView2Environment> environmentTask;

        lock (Sync)
        {
            environmentTask = _environmentTask ??= CreateEnvironmentAsync();
        }

        try
        {
            return await environmentTask.ConfigureAwait(false);
        }
        catch
        {
            lock (Sync)
            {
                if (ReferenceEquals(_environmentTask, environmentTask))
                {
                    _environmentTask = null;
                }
            }

            throw;
        }
    }

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userDataFolder = Path.Combine(localAppData, "LabelFlowStudio", "WebView2", "UserData");

        Directory.CreateDirectory(userDataFolder);

        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = SoftwareRenderingArguments
        };

        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder,
            options: options);
    }
}
