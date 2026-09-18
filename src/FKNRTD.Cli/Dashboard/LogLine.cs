using System.Text.Json;

namespace FKNRTD.Dashboard;

/// <summary>What a line of agent output turns out to be, which decides how it is coloured.</summary>
internal enum LogKind
{
    /// <summary>Ordinary output, shown as it arrived.</summary>
    Plain,

    /// <summary>Something the agent said in words.</summary>
    Said,

    /// <summary>Something the agent did: a tool call, a command, an edit.</summary>
    Did,

    /// <summary>A result, a verdict, or the end of a turn.</summary>
    Finished,

    /// <summary>An error the agent reported.</summary>
    Failed,

    /// <summary>Housekeeping the operator does not need: session ids, token counts, keepalives.</summary>
    Noise
}

/// <summary>One line of a stage log, ready to draw.</summary>
internal readonly record struct LogLine(string Text, LogKind Kind);

/// <summary>
/// Turns a line of raw agent output into something a person can read.
/// </summary>
/// <remarks>
/// The shipped agents are launched with machine-readable output so that their progress can be
/// followed - Claude with <c>--output-format stream-json</c>, Codex with its own JSON events - and
/// the log file is their stdout exactly as it arrived. That is the right thing to keep on disk and
/// the wrong thing to put on a screen: pressing L on a running task showed a wall of JSON, one
/// object per line, in which the sentence the agent had just written was a quoted field somewhere
/// past column ninety.
///
/// Nothing here changes what is stored. This is a reading of the same bytes, and any line it does
/// not recognise is shown exactly as it is, so an agent whose format is unknown is no worse off
/// than before.
/// </remarks>
internal static class LogFormat
{
    /// <summary>The longest a single formatted line is allowed to get before it is cut.</summary>
    private const int MaximumLength = 2000;

    /// <summary>Reads one raw line. Never throws: unrecognised input comes back as it went in.</summary>
    public static LogLine Read(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new LogLine(string.Empty, LogKind.Plain);
        }

        var trimmed = raw.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is not ('{' or '['))
        {
            return new LogLine(AsIs(raw), LogKind.Plain);
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return Read(document.RootElement) ?? new LogLine(AsIs(raw), LogKind.Plain);
        }
        catch (JsonException)
        {
            // A partially written line, or output that merely starts with a brace.
            return new LogLine(AsIs(raw), LogKind.Plain);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            // Well-formed JSON can still hold text that cannot be read back: an unpaired surrogate
            // parses and then throws on the way out. This is the log view, which reads whatever an
            // agent wrote - the one place that must not be able to take the dashboard down.
            return new LogLine(AsIs(raw), LogKind.Plain);
        }
    }

    private static LogLine? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = Text(root, "type") ?? Text(root, "method") ?? string.Empty;

        // Claude's session banner and its token accounting are the two things that repeat most and
        // tell an operator watching a task the least.
        var subtype = Text(root, "subtype") ?? string.Empty;
        if (type.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            return new LogLine(
                subtype.Length == 0 ? "session started" : "session " + subtype,
                LogKind.Noise);
        }

        // A run that failed says so in any of three places, and only one of them was read. Claude
        // reports a failed result as {"type":"result","subtype":"error_during_execution",
        // "is_error":true,...} - a type of plain "result", which fell through to the success path
        // below and drew the failure with a tick beside it. A log that marks a failure as finished
        // is worse than one that says nothing.
        if (Flag(root, "is_error") ||
            type.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            subtype.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            subtype.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            // The reason is as often an object as a string: {"error":{"message":"Quota exhausted"}}
            // read as a string gave nothing, and the line fell back to naming the event type - so
            // the one sentence explaining why the run stopped was the part that was dropped.
            var message = Detail(root, "message") ?? Detail(root, "error") ?? Detail(root, "result");
            return new LogLine(
                "× " + Clip(message ?? (subtype.Length > 0 ? subtype : type)),
                LogKind.Failed);
        }

        // The final answer, which is the line most worth finding in a finished log.
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
        {
            return new LogLine("√ " + Clip(result.GetString() ?? string.Empty), LogKind.Finished);
        }

        // Claude: {"type":"assistant","message":{"content":[ ... ]}}
        if (root.TryGetProperty("message", out var message2) &&
            message2.ValueKind == JsonValueKind.Object &&
            message2.TryGetProperty("content", out var content))
        {
            return FromContent(content);
        }

        // Codex: {"type":"item.completed","item":{ ... }}
        if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
        {
            return FromItem(item);
        }

        return null;
    }

    private static LogLine? FromContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return new LogLine(Clip(content.GetString() ?? string.Empty), LogKind.Said);
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var block in content.EnumerateArray())
        {
            var kind = Text(block, "type") ?? string.Empty;
            if (kind.Equals("text", StringComparison.OrdinalIgnoreCase) &&
                Text(block, "text") is { Length: > 0 } said)
            {
                return new LogLine(Clip(said), LogKind.Said);
            }

            if (kind.Equals("tool_use", StringComparison.OrdinalIgnoreCase))
            {
                var name = Text(block, "name") ?? "a tool";
                var subject = block.TryGetProperty("input", out var input) ? Subject(input) : null;
                return new LogLine(
                    subject is null ? "> " + name : $"> {name}  {subject}",
                    LogKind.Did);
            }

            if (kind.Equals("tool_result", StringComparison.OrdinalIgnoreCase))
            {
                // A tool result carries is_error, and folding a failed one into "returned" as
                // noise hid the diagnostic on the screen somebody is watching to find out what
                // went wrong. A successful result stays noise: there is one per tool call.
                if (Flag(block, "is_error"))
                {
                    var reason = Detail(block, "content");
                    return new LogLine(
                        reason is null ? "  a tool failed" : "  a tool failed: " + Clip(reason),
                        LogKind.Failed);
                }

                return new LogLine("  returned", LogKind.Noise);
            }
        }

        return null;
    }

    private static LogLine? FromItem(JsonElement item)
    {
        var kind = Text(item, "type") ?? string.Empty;

        if (kind.Contains("command", StringComparison.OrdinalIgnoreCase) &&
            Text(item, "command") is { Length: > 0 } command)
        {
            return new LogLine("> " + Clip(command), LogKind.Did);
        }

        if (kind.Contains("agent_message", StringComparison.OrdinalIgnoreCase) &&
            Text(item, "text") is { Length: > 0 } spoken)
        {
            return new LogLine(Clip(spoken), LogKind.Said);
        }

        if (kind.Contains("plan", StringComparison.OrdinalIgnoreCase))
        {
            var text = Text(item, "text") ?? Text(item, "title") ?? Text(item, "step");
            return text is null ? null : new LogLine("plan: " + Clip(text), LogKind.Said);
        }

        if (kind.Contains("file", StringComparison.OrdinalIgnoreCase) && Subject(item) is { } path)
        {
            return new LogLine("> wrote " + path, LogKind.Did);
        }

        return null;
    }

    /// <summary>
    /// The one detail worth putting beside a tool's name: which file it is about, or the command it
    /// is about to run. Everything else in a tool's input is too long for a single row.
    /// </summary>
    private static string? Subject(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in new[]
                 {
                     "file_path", "filePath", "path", "notebook_path", "command", "pattern", "url", "query"
                 })
        {
            if (input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                value.GetString() is { Length: > 0 } text)
            {
                return Clip(text, 160);
            }
        }

        return null;
    }

    /// <summary>
    /// A line that is not an agent's structured output, kept as it was written. Build and test tools
    /// indent to show structure - a failing assertion's expected and actual values line up under
    /// each other - and collapsing that whitespace throws away the only formatting they have.
    /// </summary>
    private static string AsIs(string value)
    {
        var expanded = value.Replace("	", "    ", StringComparison.Ordinal).TrimEnd();
        return expanded.Length <= MaximumLength ? expanded : expanded[..MaximumLength] + "...";
    }

    /// <summary>Whether a JSON property is literally <c>true</c>. Absent or false reads as false.</summary>
    private static bool Flag(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Reads a property that carries a human sentence, wherever the agent chose to put it: the
    /// value itself, an object holding it, or the first block of a list of them.
    /// </summary>
    /// <remarks>
    /// <see cref="Text"/> accepts a string and nothing else, which is right for a field like
    /// "type" and wrong for a reason: every agent nests those differently, and reading only the
    /// flat case dropped the sentence and kept the label.
    /// </remarks>
    private static string? Detail(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return Detail(value);
    }

    private static string? Detail(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            case JsonValueKind.Object:
                return Text(value, "message") ?? Text(value, "text") ?? Text(value, "error")
                    ?? Text(value, "content");
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (Detail(item) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Collapses a value onto one line and caps its length. A log row is one line by definition, and
    /// an agent's message can be several paragraphs.
    /// </summary>
    private static string Clip(string value, int maximum = MaximumLength)
    {
        var flattened = value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('\t', ' ');
        while (flattened.Contains("  ", StringComparison.Ordinal))
        {
            flattened = flattened.Replace("  ", " ", StringComparison.Ordinal);
        }

        flattened = flattened.Trim();
        return flattened.Length <= maximum ? flattened : flattened[..maximum] + "...";
    }
}
