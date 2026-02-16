using LabelFlowStudio.Templates;
using System.IO;
using System.Text;

namespace LabelFlowStudio.Desktop.Templates;

public static class EndLabelTemplateStore
{
    private const string TemplatesFolderName = "Templates";
    private const string EndLabelFileName = "EndLabel.html";

    public static string GetTemplatePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "LabelFlowStudio", TemplatesFolderName, EndLabelFileName);
    }

    public static async Task<string> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        var path = GetTemplatePath();
        var dir = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (!File.Exists(path))
        {
            var defaultPath = Path.Combine(AppContext.BaseDirectory, TemplatesFolderName, EndLabelFileName);

            if (File.Exists(defaultPath))
            {
                var defaultHtml = await File.ReadAllTextAsync(defaultPath, Encoding.UTF8, cancellationToken);
                await File.WriteAllTextAsync(path, defaultHtml, new UTF8Encoding(false), cancellationToken);
            }
        }

        return await EditableTemplateFileManager.LoadOrCreateAsync(
            path,
            TemplateDefaults.GetEndLabelHtml,
            cancellationToken
        );
    }
}
