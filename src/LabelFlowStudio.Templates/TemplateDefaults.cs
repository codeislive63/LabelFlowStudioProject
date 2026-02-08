using System.Reflection;

namespace LabelFlowStudio.Templates;

public static class TemplateDefaults
{
    public static string GetEndLabelHtml()
    {
        return ReadEmbeddedTextEndingWith("EndLabel.EndLabel.html");
    }

    public static string GetStuffingSheetHtml()
    {
        return ReadEmbeddedTextEndingWith("StuffingSheet.StuffingSheet.html");
    }

    public static string GetEmptyPageHtml()
    {
        return ReadEmbeddedTextEndingWith("EmptyPage.EmptyPage.html");
    }

    private static string ReadEmbeddedTextEndingWith(string resourceNameEnding)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(resourceNameEnding, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Template resource not found: *{resourceNameEnding}");

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Template resource stream not found: {resourceName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
