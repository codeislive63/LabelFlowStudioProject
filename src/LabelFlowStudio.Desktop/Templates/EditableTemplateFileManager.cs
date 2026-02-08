using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LabelFlowStudio.Desktop.Templates;

public static class EditableTemplateFileManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<string> LoadOrCreateAsync(
        string templatePath,
        Func<string> getDefaultTemplate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(templatePath));
        }

        if (getDefaultTemplate is null)
        {
            throw new ArgumentNullException(nameof(getDefaultTemplate));
        }

        var directory = Path.GetDirectoryName(templatePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Cannot determine template directory for path: {templatePath}");
        }

        Directory.CreateDirectory(directory);

        var defaultTemplate = NormalizeNewlines(getDefaultTemplate());
        var defaultHash = ComputeSha256Hex(defaultTemplate);

        var metaPath = GetMetaPath(templatePath);
        var newPath = GetNewTemplatePath(templatePath);

        if (!File.Exists(templatePath))
        {
            await WriteUtf8NoBomAsync(templatePath, defaultTemplate, cancellationToken);
            await WriteMetaAsync(metaPath, new TemplateMeta { BaselineDefaultHash = defaultHash }, cancellationToken);
            TryDeleteFile(newPath);

            return defaultTemplate;
        }

        var currentTemplate = await File.ReadAllTextAsync(templatePath, Encoding.UTF8, cancellationToken);
        var currentHash = ComputeSha256Hex(NormalizeNewlines(currentTemplate));

        TemplateMeta? meta = await TryReadMetaAsync(metaPath, cancellationToken);
        var baselineHash = meta?.BaselineDefaultHash;

        // If meta is missing but the file already matches current default, initialize meta.
        if (string.IsNullOrWhiteSpace(baselineHash) && currentHash == defaultHash)
        {
            await WriteMetaAsync(metaPath, new TemplateMeta { BaselineDefaultHash = defaultHash }, cancellationToken);
            TryDeleteFile(newPath);

            return currentTemplate;
        }

        var isUnmodified = !string.IsNullOrWhiteSpace(baselineHash) && currentHash == baselineHash;

        if (isUnmodified)
        {
            if (currentHash != defaultHash)
            {
                // Auto-update to the latest default if user didn't edit the file.
                await WriteUtf8NoBomAsync(templatePath, defaultTemplate, cancellationToken);
                await WriteMetaAsync(metaPath, new TemplateMeta { BaselineDefaultHash = defaultHash }, cancellationToken);
                TryDeleteFile(newPath);

                return defaultTemplate;
            }

            // File is still unmodified and already matches latest default.
            if (baselineHash != defaultHash)
            {
                await WriteMetaAsync(metaPath, new TemplateMeta { BaselineDefaultHash = defaultHash }, cancellationToken);
            }

            TryDeleteFile(newPath);
            return currentTemplate;
        }

        // File was edited by user. If default changed since baseline, drop a side-by-side "new" file.
        var shouldWriteNew = !string.IsNullOrWhiteSpace(baselineHash) ? baselineHash != defaultHash : currentHash != defaultHash;

        if (shouldWriteNew)
        {
            await WriteNewIfDifferentAsync(newPath, defaultTemplate, cancellationToken);
        }

        return currentTemplate;
    }

    private static string GetMetaPath(string templatePath)
    {
        var directory = Path.GetDirectoryName(templatePath)!;
        var fileName = Path.GetFileNameWithoutExtension(templatePath);
        return Path.Combine(directory, $"{fileName}.meta.json");
    }

    private static string GetNewTemplatePath(string templatePath)
    {
        var directory = Path.GetDirectoryName(templatePath)!;
        var fileName = Path.GetFileNameWithoutExtension(templatePath);
        var extension = Path.GetExtension(templatePath);
        return Path.Combine(directory, $"{fileName}.new{extension}");
    }

    private static async Task<TemplateMeta?> TryReadMetaAsync(string metaPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(metaPath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(metaPath, Encoding.UTF8, cancellationToken);
            return JsonSerializer.Deserialize<TemplateMeta>(json, JsonOptions);
        }
        catch
        {
            // Corrupted meta shouldn't block template loading.
            return null;
        }
    }

    private static async Task WriteMetaAsync(string metaPath, TemplateMeta meta, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        await WriteUtf8NoBomAsync(metaPath, json, cancellationToken);
    }

    private static async Task WriteNewIfDifferentAsync(string newPath, string defaultTemplate, CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(newPath))
            {
                var existing = await File.ReadAllTextAsync(newPath, Encoding.UTF8, cancellationToken);

                if (ComputeSha256Hex(NormalizeNewlines(existing)) == ComputeSha256Hex(defaultTemplate))
                {
                    return;
                }
            }
        }
        catch
        {
            // Ignore read issues and overwrite.
        }

        await WriteUtf8NoBomAsync(newPath, defaultTemplate, cancellationToken);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore delete errors.
        }
    }

    private static async Task WriteUtf8NoBomAsync(string path, string content, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
    }

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string ComputeSha256Hex(string value)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private sealed class TemplateMeta
    {
        public string? BaselineDefaultHash { get; set; }
    }
}
