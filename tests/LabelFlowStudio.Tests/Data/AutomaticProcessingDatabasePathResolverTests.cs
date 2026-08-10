using LabelFlowStudio.Data.Statistics;
using Microsoft.Extensions.Configuration;

namespace LabelFlowStudio.Application.Tests.Data;

public sealed class AutomaticProcessingDatabasePathResolverTests
{
    [Fact]
    public void Resolve_DefaultsToLocalAppData()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Environment.SpecialFolder? requestedFolder = null;
        string basePath = Path.GetPathRoot(Environment.SystemDirectory)!;

        string result = AutomaticProcessingDatabasePathResolver.Resolve(
            configuration,
            folder =>
            {
                requestedFolder = folder;
                return basePath;
            });

        Assert.Equal(Environment.SpecialFolder.LocalApplicationData, requestedFolder);
        Assert.Equal(
            Path.Combine(basePath, "LabelFlowStudio", "data", "LabelFlowStudio.db"),
            result);
    }

    [Fact]
    public void Resolve_UsesProgramData_WhenConfigured()
    {
        var configuration = BuildConfiguration(("LocalStorage:Scope", "ProgramData"));
        Environment.SpecialFolder? requestedFolder = null;
        string basePath = Path.GetPathRoot(Environment.SystemDirectory)!;

        AutomaticProcessingDatabasePathResolver.Resolve(
            configuration,
            folder =>
            {
                requestedFolder = folder;
                return basePath;
            });

        Assert.Equal(Environment.SpecialFolder.CommonApplicationData, requestedFolder);
    }

    [Fact]
    public void Resolve_AbsoluteOverrideTakesPrecedenceOverScope()
    {
        string expectedPath = Path.Combine(
            Path.GetTempPath(),
            "LabelFlowStudio.Tests",
            Guid.NewGuid().ToString("N"),
            "custom.db");
        var configuration = BuildConfiguration(
            ("LocalStorage:Scope", "Unsupported"),
            ("LocalStorage:DatabasePath", expectedPath));

        string result = AutomaticProcessingDatabasePathResolver.Resolve(
            configuration,
            _ => throw new InvalidOperationException("Folder resolver must not be used."));

        Assert.Equal(Path.GetFullPath(expectedPath), result);
    }

    [Theory]
    [InlineData("LocalStorage:Scope", "MachineWide")]
    [InlineData("LocalStorage:DatabasePath", "relative\\LabelFlowStudio.db")]
    public void Resolve_RejectsUnsupportedConfiguration(string key, string value)
    {
        var configuration = BuildConfiguration((key, value));

        Assert.Throws<InvalidOperationException>(() =>
            AutomaticProcessingDatabasePathResolver.Resolve(
                configuration,
                _ => Path.GetPathRoot(Environment.SystemDirectory)!));
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
    }
}
