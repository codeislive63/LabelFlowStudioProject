using LabelFlowStudio.Templates;
using System.IO;
using System.Text;

namespace LabelFlowStudio.Desktop.Templates;

public static class EmptyPageTemplateStore
{
    private const string TemplatesFolderName = "Templates";
    private const string EmptyPageFileName = "EmptyPage.html";

    public static string GetTemplatePath()
    {
        return Path.Combine(AppContext.BaseDirectory, TemplatesFolderName, EmptyPageFileName);
    }

    public static async Task<string> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        var path = GetTemplatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            var defaultTemplate = TemplateDefaults.GetEmptyPageHtml();
            await File.WriteAllTextAsync(path, defaultTemplate, Encoding.UTF8, cancellationToken);
        }

        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
    }
}
