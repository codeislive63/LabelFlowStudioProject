using LabelFlowStudio.Templates;
using System.IO;

namespace LabelFlowStudio.Desktop.Templates;

public static class EndLabelTemplateStore
{
    private const string TemplatesFolderName = "Templates";
    private const string EndLabelFileName = "EndLabel.html";

    public static string GetTemplatePath()
    {
        return Path.Combine(AppContext.BaseDirectory, TemplatesFolderName, EndLabelFileName);
    }

    public static Task<string> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        return EditableTemplateFileManager.LoadOrCreateAsync(
            GetTemplatePath(),
            TemplateDefaults.GetEndLabelHtml,
            cancellationToken
        );
    }
}
