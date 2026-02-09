using System.Text;
using LabelFlowStudio.Desktop.Templates;

namespace LabelFlowStudio.Application.Tests.Templates;

public sealed class EditableTemplateFileManagerTests
{
    [Fact]
    public async Task LoadOrCreateAsync_CreatesTemplateAndMeta_WhenMissing()
    {
        var directory = CreateTempDirectory();
        var templatePath = Path.Combine(directory, "EndLabel.html");

        var template = await EditableTemplateFileManager.LoadOrCreateAsync(
            templatePath,
            getDefaultTemplate: () => "<html>v1</html>",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal("<html>v1</html>", NormalizeNewlines(template));
        Assert.True(File.Exists(templatePath));

        var metaPath = Path.Combine(directory, "EndLabel.meta.json");
        Assert.True(File.Exists(metaPath));

        var metaJson = await File.ReadAllTextAsync(metaPath, Encoding.UTF8);
        Assert.Contains("baselineDefaultHash", metaJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadOrCreateAsync_AutoUpdatesTemplate_WhenUnmodifiedAndDefaultChanged()
    {
        var directory = CreateTempDirectory();
        var templatePath = Path.Combine(directory, "StuffingSheet.html");

        await EditableTemplateFileManager.LoadOrCreateAsync(
            templatePath,
            getDefaultTemplate: () => "<html>v1</html>",
            cancellationToken: CancellationToken.None
        );

        var loaded = await EditableTemplateFileManager.LoadOrCreateAsync(
            templatePath,
            getDefaultTemplate: () => "<html>v2</html>",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal("<html>v2</html>", NormalizeNewlines(loaded));

        var fileText = await File.ReadAllTextAsync(templatePath, Encoding.UTF8);
        Assert.Equal("<html>v2</html>", NormalizeNewlines(fileText));

        var newPath = Path.Combine(directory, "StuffingSheet.new.html");
        Assert.False(File.Exists(newPath));
    }

    [Fact]
    public async Task LoadOrCreateAsync_WritesSideBySideNewFile_WhenUserEditedAndDefaultChanged()
    {
        var directory = CreateTempDirectory();
        var templatePath = Path.Combine(directory, "EmptyPage.html");

        await EditableTemplateFileManager.LoadOrCreateAsync(
            templatePath,
            getDefaultTemplate: () => "<html>v1</html>",
            cancellationToken: CancellationToken.None
        );

        await File.WriteAllTextAsync(templatePath, "<html>user</html>", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var loaded = await EditableTemplateFileManager.LoadOrCreateAsync(
            templatePath,
            getDefaultTemplate: () => "<html>v2</html>",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal("<html>user</html>", NormalizeNewlines(loaded));

        var newPath = Path.Combine(directory, "EmptyPage.new.html");
        Assert.True(File.Exists(newPath));

        var newText = await File.ReadAllTextAsync(newPath, Encoding.UTF8);
        Assert.Equal("<html>v2</html>", NormalizeNewlines(newText));
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LabelFlowStudio.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
