using System.Text.Json;
using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.Tests.TestData;

public static class LabelRecordTestData
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<LabelRecord> LoadByTenam(string tenam)
    {
        if (string.IsNullOrWhiteSpace(tenam))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(tenam));
        }

        var path = Path.Combine(AppContext.BaseDirectory, "TestData", $"label_records_{tenam}.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Test data not found for TENAM={tenam}", path);
        }

        var json = File.ReadAllText(path);
        var records = JsonSerializer.Deserialize<List<LabelRecord>>(json, JsonOptions);

        return records ?? new List<LabelRecord>();
    }
}
