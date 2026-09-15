using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FKNRTD.Services;

public sealed class ClaudeIntegrationService
{
    public async Task<string> InstallStatusLineAsync(
        bool projectScope,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var settingsPath = projectScope
            ? Path.Combine(Environment.CurrentDirectory, ".claude", "settings.json")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude",
                "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        JsonObject settings;
        if (File.Exists(settingsPath))
        {
            var existing = await File.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false);
            settings = JsonNode.Parse(
                           existing,
                           nodeOptions: null,
                           documentOptions: new JsonDocumentOptions
                           {
                               AllowTrailingCommas = true,
                               CommentHandling = JsonCommentHandling.Skip
                           })?.AsObject()
                ?? throw new InvalidDataException($"Claude settings are not a JSON object: {settingsPath}");
            if (settings["statusLine"] is not null && !force)
            {
                throw new InvalidOperationException(
                    "Claude already has a status line. Re-run with -force to replace it after a backup is created.");
            }

            var backup = settingsPath + ".fknrtd-backup-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
            File.Copy(settingsPath, backup, overwrite: false);
        }

        else
        {
            settings = new JsonObject();
        }

        settings["statusLine"] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = BuildCommand(),
            ["padding"] = 0,
            ["refreshInterval"] = 2
        };

        var json = settings.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        var temporary = settingsPath + ".fknrtd-" + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, json + Environment.NewLine, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporary, settingsPath, overwrite: true);
        return settingsPath;
    }

    internal static string BuildCommand()
    {
        var installedShim = ExecutableLocator.Find("fknrtd");
        if (!string.IsNullOrWhiteSpace(installedShim))
        {
            return Quote(installedShim) + " telemetry claude-statusline";
        }

        var processPath = Environment.ProcessPath ?? "dotnet";
        var assemblyPath = Assembly.GetEntryAssembly()?.Location ?? string.Empty;
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return Quote(processPath) + " " + Quote(assemblyPath) + " telemetry claude-statusline";
        }

        return Quote(processPath) + " telemetry claude-statusline";
    }

    private static string Quote(string value)
    {
        if (OperatingSystem.IsWindows())
        {
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
}
