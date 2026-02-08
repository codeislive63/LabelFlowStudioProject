using LabelFlowStudio.Templates;
using System.IO;

namespace LabelFlowStudio.Desktop.Templates;

public static class StuffingSheetTemplateStore
{
    private const string TemplatesFolderName = "Templates";
    private const string StuffingSheetFileName = "StuffingSheet.html";

    public static string GetTemplatePath()
    {
        return Path.Combine(AppContext.BaseDirectory, TemplatesFolderName, StuffingSheetFileName);
    }

    public static Task<string> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        return EditableTemplateFileManager.LoadOrCreateAsync(
            GetTemplatePath(),
            TemplateDefaults.GetStuffingSheetHtml,
            cancellationToken
        );
    }
}
