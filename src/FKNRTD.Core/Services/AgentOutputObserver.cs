using System.Text.Json;
using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class AgentOutputObserver
{
    private readonly StateStore _store;
    private readonly AgentRuntimeState _state;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastPersistedAt = DateTimeOffset.MinValue;

    public AgentOutputObserver(StateStore store, AgentRuntimeState state)
    {
        _store = store;
        _state = state;
    }

    public async Task ObserveAsync(string line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!TryObserveJson(line))
            {
                ObserveText(line, isError);
            }

            _state.UpdatedAt = DateTimeOffset.UtcNow;
            if (_state.UpdatedAt - _lastPersistedAt >= TimeSpan.FromMilliseconds(400))
            {
                await _store.SaveAgentRuntimeAsync(_state).ConfigureAwait(false);
                _lastPersistedAt = _state.UpdatedAt;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryObserveJson(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = GetString(root, "type") ?? GetString(root, "method") ?? string.Empty;

            if (type.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                _state.State = AgentActivityState.Failed;
            }
            else if (type.Contains("completed", StringComparison.OrdinalIgnoreCase) &&
                     type.StartsWith("turn", StringComparison.OrdinalIgnoreCase))
            {
                _state.State = AgentActivityState.Completed;
            }

            if (TryGetProperty(root, "item", out var item))
            {
                ObserveItem(item);
            }

            if (TryGetProperty(root, "message", out var message) &&
                TryGetProperty(message, "content", out var content))
            {
                ObserveContent(content);
            }

            if (TryGetProperty(root, "result", out var result) && result.ValueKind == JsonValueKind.String)
            {
                _state.Intent = Preview(result.GetString() ?? string.Empty, 100);
                _state.IntentSource = "result";
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void ObserveItem(JsonElement item)
    {
        var itemType = GetString(item, "type") ?? string.Empty;
        if (itemType.Contains("command", StringComparison.OrdinalIgnoreCase))
        {
            var command = GetString(item, "command");
            if (!string.IsNullOrWhiteSpace(command))
            {
                _state.Intent = Preview(command, 100);
                _state.IntentSource = "command";
            }
        }
        else if (itemType.Contains("plan", StringComparison.OrdinalIgnoreCase))
        {
            var text = FirstString(item, ["text", "title", "step"]);
            if (!string.IsNullOrWhiteSpace(text))
            {
                _state.Intent = Preview(text, 100);
                _state.IntentSource = "plan";
            }
        }
        else if (itemType.Contains("agent_message", StringComparison.OrdinalIgnoreCase))
        {
            var text = GetString(item, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                _state.Intent = Preview(text, 100);
                _state.IntentSource = "message";
            }
        }

        CollectPaths(item);
    }

    private void ObserveContent(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var block in content.EnumerateArray())
        {
            var type = GetString(block, "type") ?? string.Empty;
            if (type.Equals("tool_use", StringComparison.OrdinalIgnoreCase))
            {
                var name = GetString(block, "name") ?? "tool";
                _state.Intent = $"Using {name}";
                _state.IntentSource = "tool";
                if (TryGetProperty(block, "input", out var input))
                {
                    CollectPaths(input);
                }
            }
            else if (type.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                var text = GetString(block, "text");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _state.Intent = Preview(text, 100);
                    _state.IntentSource = "message";
                }
            }
        }
    }

    private void CollectPaths(JsonElement element)
    {
        Walk(element, (name, value) =>
        {
            if (value.ValueKind != JsonValueKind.String ||
                !name.Equals("path", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("file_path", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("filePath", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var path = value.GetString();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            path = MakeRelative(path);
            if (!_state.TouchedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _state.TouchedPaths.Add(path);
            }
        });
    }

    private void ObserveText(string line, bool isError)
    {
        var text = line.Trim();
        if (isError && (text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        text.Contains("failed", StringComparison.OrdinalIgnoreCase)))
        {
            _state.Intent = Preview(text, 100);
            _state.IntentSource = "stderr";
            return;
        }

        if (!isError && text.Length > 3)
        {
            _state.Intent = Preview(text, 100);
            _state.IntentSource = "output";
        }
    }

    private string MakeRelative(string path)
    {
        try
        {
            return Path.IsPathRooted(path)
                ? Path.GetRelativePath(_state.WorkingDirectory, path).Replace('\\', '/')
                : path.Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return path.Replace('\\', '/');
        }
    }

    private static void Walk(JsonElement element, Action<string, JsonElement> visit)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                visit(property.Name, property.Value);
                Walk(property.Value, visit);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Walk(item, visit);
            }
        }
    }

    private static string? FirstString(JsonElement element, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var result = FindString(element, name);
            if (!string.IsNullOrWhiteSpace(result))
            {
                return result;
            }
        }

        return null;
    }

    private static string? FindString(JsonElement element, string name)
    {
        string? found = null;
        Walk(element, (propertyName, value) =>
        {
            if (found is null && propertyName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                value.ValueKind == JsonValueKind.String)
            {
                found = value.GetString();
            }
        });
        return found;
    }

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string Preview(string value, int maxLength)
    {
        var singleLine = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= maxLength
            ? singleLine
            : singleLine[..Math.Max(1, maxLength - 1)] + "…";
    }
}
