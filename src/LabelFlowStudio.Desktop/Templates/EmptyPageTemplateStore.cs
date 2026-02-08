using LabelFlowStudio.Templates;
using System.IO;

namespace LabelFlowStudio.Desktop.Templates;

public static class EmptyPageTemplateStore
{
    private const string TemplatesFolderName = "Templates";
    private const string EmptyPageFileName = "EmptyPage.html";

    public static string GetTemplatePath()
    {
        return Path.Combine(AppContext.BaseDirectory, TemplatesFolderName, EmptyPageFileName);
    }

    public static Task<string> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        return EditableTemplateFileManager.LoadOrCreateAsync(
            GetTemplatePath(),
            TemplateDefaults.GetEmptyPageHtml,
            cancellationToken
        );
    }
}
