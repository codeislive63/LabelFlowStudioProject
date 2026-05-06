using LabelFlowStudio.Desktop.Logging;

namespace LabelFlowStudio.Application.Tests.Desktop.Logging;

public sealed class LogPathResolverTests
{
    [Fact]
    public void GetLogDirectory_ReturnsExistingDirectory()
    {
        var directory = LogPathResolver.GetLogDirectory();

        Assert.False(string.IsNullOrWhiteSpace(directory));
        Assert.True(Directory.Exists(directory));
        Assert.EndsWith("logs", directory.TrimEnd(Path.DirectorySeparatorChar));
    }
}
