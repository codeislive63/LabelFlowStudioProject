using System.IO;
using System.Security;

namespace LabelFlowStudio.Desktop.Logging;

public static class LogPathResolver
{
    public static string GetLogDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var nearExeDirectory = Path.Combine(baseDirectory, "logs");

        if (TryEnsureDirectory(nearExeDirectory))
        {
            return nearExeDirectory;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fallbackDirectory = Path.Combine(localAppData, "LabelFlowStudio", "logs");

        Directory.CreateDirectory(fallbackDirectory);
        return fallbackDirectory;
    }

    private static bool TryEnsureDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
