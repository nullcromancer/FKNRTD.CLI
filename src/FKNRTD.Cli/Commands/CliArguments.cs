using FKNRTD.Help;

namespace FKNRTD.Commands;

internal sealed class CliArguments
{
    private readonly Dictionary<string, List<string>> _options =
        new(StringComparer.OrdinalIgnoreCase);

    public CliArguments(IEnumerable<string> arguments)
    {
        var input = arguments.ToArray();
        var (valueless, valueTaking) = DocumentedOptions(input);
        var positionalOnly = false;
        for (var index = 0; index < input.Length; index++)
        {
            var token = input[index];
            if (token == "--" && !positionalOnly)
            {
                // Only the first one ends the options. A later `--` is an argument like any other,
                // and discarding it left `fknrtd init -- --` with no folder to act on.
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

                    // An option documented as taking no value does not eat the next word. Every
                    // option used to: `fknrtd task show -json FKN-20260918-...` read the task ID
                    // as the value of -json and then refused the line for having no task ID, and
                    // `fknrtd init -standalone ./notes` initialized the wrong folder. Both are
                    // written that way in the catalog's own examples.
                    if (valueless.Contains(name))
                    {
                        value = "true";
                    }
                    else if (index + 1 < input.Length && !IsOption(input[index + 1]))
                    {
                        value = input[++index];
                    }
                    else
                    {
                        // An option that is documented as taking a value, written with none, means
                        // the empty one. The catalog says of -verify that you "pass -verify with no
                        // value only if you mean to check nothing"; inventing "true" turned that
                        // into a task whose single acceptance command was a program called true.
                        value = valueTaking.Contains(name) ? string.Empty : "true";
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

    /// <summary>
    /// The options the command on this line documents as taking no value, so that they can be read
    /// as the flags they are rather than swallowing the word after them.
    /// </summary>
    /// <remarks>
    /// The catalog already records this: an option's value hint is empty exactly when it takes no
    /// value, which is what the help page prints. Reading it from there rather than keeping a list
    /// here is what stops the parser and the help page drifting apart - the same reason the unknown
    /// option check reads the catalog instead of its own table.
    /// <para>
    /// This fails open, like that check. A command the catalog does not know keeps the old greedy
    /// reading, because refusing to parse a valid line is worse than the fault being fixed.
    /// </para>
    /// </remarks>
    private static (HashSet<string> Valueless, HashSet<string> ValueTaking) DocumentedOptions(
        IReadOnlyList<string> input)
    {
        var flags = new HashSet<string>(AlwaysValueless, StringComparer.OrdinalIgnoreCase);
        var takesValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The command words are the first two tokens that are not options or option values:
        // "task", then "show". Stopping at the first option instead meant that `fknrtd -root .
        // task show -json <id>` found no command at all, so every option on it fell back to the
        // greedy reading - the fault this method exists to fix, reappearing whenever somebody
        // selected the workspace before naming the command.
        var words = new List<string>(2);
        for (var index = 0; index < input.Count && words.Count < 2; index++)
        {
            var token = input[index];
            if (token == "--")
            {
                break;
            }

            if (IsOption(token))
            {
                // A global option that takes a value takes the token after it with it. Only the
                // universal ones can be known here, which is the point: the rest are read against
                // the command these words are being gathered to find.
                if (!token.Contains('=', StringComparison.Ordinal) &&
                    GlobalValueTaking.Contains(token.TrimStart('-')))
                {
                    index++;
                }

                continue;
            }

            words.Add(token.ToLowerInvariant());
        }

        var entry = words.Count == 2 ? CommandCatalog.Exact(words[0] + " " + words[1]) : null;
        entry ??= words.Count >= 1 ? CommandCatalog.Exact(words[0]) : null;
        if (entry is null)
        {
            return (flags, takesValue);
        }

        foreach (var option in entry.Options)
        {
            if (!option.Name.StartsWith('-'))
            {
                continue;
            }

            if (option.ValueHint.Length == 0)
            {
                flags.Add(option.Name.TrimStart('-'));
            }
            else
            {
                takesValue.Add(option.Name.TrimStart('-'));
            }
        }

        // The command's own catalog entry wins over the universal list. -color forces ANSI
        // everywhere and is also `agent add -color <colour>`, so treating it as a flag on every
        // line made a documented option stop taking its value.
        flags.ExceptWith(takesValue);
        return (flags, takesValue);
    }

    /// <summary>
    /// Flags accepted everywhere, which the catalog lists per command rather than centrally. They
    /// are named here so that they still parse as flags on a line whose command is unknown - which
    /// is precisely when somebody is reaching for -help.
    /// </summary>
    private static readonly string[] AlwaysValueless =
        ["no-color", "color", "help", "h", "version", "v", "json"];

    /// <summary>
    /// Options accepted before the command that take the word after them, so that the command can
    /// still be found behind one. Only <c>-root</c> qualifies; every other option is read against
    /// the command rather than before it.
    /// </summary>
    private static readonly HashSet<string> GlobalValueTaking =
        new(["root"], StringComparer.OrdinalIgnoreCase);

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
