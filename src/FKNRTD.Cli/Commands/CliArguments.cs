namespace FKNRTD.Commands;

internal sealed class CliArguments
{
    private readonly Dictionary<string, List<string>> _options =
        new(StringComparer.OrdinalIgnoreCase);

    public CliArguments(IEnumerable<string> arguments)
    {
        var input = arguments.ToArray();
        var positionalOnly = false;
        for (var index = 0; index < input.Length; index++)
        {
            var token = input[index];
            if (token == "--")
            {
                positionalOnly = true;
                continue;
            }

            if (!positionalOnly && IsOption(token))
            {
                var option = token.TrimStart('-');
                var separator = option.IndexOf('=');
                string name;
                string value;
                if (separator >= 0)
                {
                    name = option[..separator];
                    value = option[(separator + 1)..];
                }
                else
                {
                    name = option;
                    if (index + 1 < input.Length && !IsOption(input[index + 1]))
                    {
                        value = input[++index];
                    }
                    else
                    {
                        value = "true";
                    }
                }

                if (!_options.TryGetValue(name, out var values))
                {
                    values = [];
                    _options[name] = values;
                    Supplied.Add(name);
                }

                values.Add(value);
            }
            else
            {
                Positionals.Add(token);
            }
        }
    }

    public List<string> Positionals { get; } = [];

    /// <summary>
    /// Every option name given on the command line, once each, in the order first written.
    /// </summary>
    /// <remarks>
    /// Commands read the options they know by name and nothing looks at the rest, so an option
    /// nobody reads used to be silently discarded: <c>fknrtd task list -jsno</c> printed a table
    /// and exited 0, which is a script asking for JSON and being told everything went well. This
    /// is what the dispatcher checks against the command's documented options before running it.
    /// </remarks>
    public List<string> Supplied { get; } = [];

    public string Command => Positional(0)?.ToLowerInvariant() ?? string.Empty;

    public string Subcommand => Positional(1)?.ToLowerInvariant() ?? string.Empty;

    public string? Positional(int index) => index >= 0 && index < Positionals.Count
        ? Positionals[index]
        : null;

    public string? Get(string name) => _options.TryGetValue(name, out var values)
        ? values.LastOrDefault()
        : null;

    public IReadOnlyList<string> GetMany(string name) => _options.TryGetValue(name, out var values)
        ? values
        : [];

    public bool Has(string name)
    {
        var value = Get(name);
        return value is not null &&
               !value.Equals("false", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("0", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    public int? GetInt(string name)
    {
        var value = Get(name);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, out var result)
            ? result
            : throw new ArgumentException($"Option -{name} must be an integer.");
    }

    public double? GetDouble(string name)
    {
        var value = Get(name);
        if (value is null)
        {
            return null;
        }

        return double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var result) && double.IsFinite(result)
            ? result
            : throw new ArgumentException($"Option -{name} must be a number.");
    }

    private static bool IsOption(string value) => value.Length > 1 && value[0] == '-' &&
                                                   !double.TryParse(
                                                       value,
                                                       System.Globalization.NumberStyles.Float,
                                                       System.Globalization.CultureInfo.InvariantCulture,
                                                       out _);
}
