using FKNRTD.Dashboard;
using FKNRTD.Help;

namespace FKNRTD.Commands;

/// <summary>
/// <c>fknrtd help</c>. Rendered from <see cref="CommandCatalog"/> rather than from a hand-written
/// block of text, so the help cannot describe a command that was removed or omit one that was
/// added. <c>fknrtd help &lt;command&gt;</c> prints the full entry for one command, including what
/// it changes on disk and what to do next.
/// </summary>
internal static class HelpCommand
{
    /// <summary>
    /// How wide the command column is on the quick reference. Named rather than repeated so the
    /// row and the summary that follows it cannot disagree about where the second column starts.
    /// </summary>
    private const int NameColumn = 26;

    public static int Execute(CliArguments arguments, bool useColor)
    {
        var width = Math.Clamp(Screen.Width(88) - 2, 40, 96);
        var topic = string.Join(' ', arguments.Positionals.Skip(1)).Trim();

        if (topic.Length == 0)
        {
            Overview(width, useColor);
            return 0;
        }

        var entry = CommandCatalog.Find(topic);
        if (entry is not null)
        {
            Detail(entry, width, useColor);
            return 0;
        }

        var group = CommandCatalog.Groups
            .FirstOrDefault(name => name.Equals(topic, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            Group(group, width, useColor);
            return 0;
        }

        // A word that is not a command is very often a concept, so the two lookups are offered
        // together rather than sending the operator away to guess which one it was.
        var term = Glossary.Find(topic);
        if (term is not null)
        {
            Console.WriteLine($"'{topic}' is not a command. It is a concept:");
            Console.WriteLine();
            foreach (var line in Text.Wrap(term.Summary, width))
            {
                Console.WriteLine(line);
            }

            Console.WriteLine();
            Console.WriteLine($"Read it in full with: fknrtd explain {term.Term}");
            return 0;
        }

        var suggestions = CommandCatalog.Search(topic);
        Console.Error.WriteLine($"There is no '{topic}' command.");
        if (suggestions.Count > 0)
        {
            Console.Error.WriteLine("Did you mean:");
            foreach (var candidate in suggestions.Take(5))
            {
                Console.Error.WriteLine($"  fknrtd {candidate.Name,-24} {candidate.Summary}");
            }
        }
        else
        {
            Console.Error.WriteLine("Run 'fknrtd help' for every command, or 'fknrtd explain' for every term.");
        }

        return 2;
    }

    private static void Overview(int width, bool useColor)
    {
        Write("FKNRTD COMMAND CENTER", Theme.Cyan, useColor, bold: true);
        Console.WriteLine();
        foreach (var line in Text.Wrap(Glossary.Find("fknrtd")?.Summary ?? string.Empty, width))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
        Write("NEW HERE", Theme.Green, useColor, bold: true);
        Console.WriteLine();
        Console.WriteLine("  fknrtd                    Open the command center here. It explains itself as you go.");
        Console.WriteLine("  fknrtd doctor             Check that everything a run depends on is installed.");
        Console.WriteLine("  fknrtd task new           Describe a piece of work through a guided, explained form.");
        Console.WriteLine("  fknrtd explain <word>     Look up any term this product uses.");
        Console.WriteLine("  fknrtd portal             Write the full offline guide as a single HTML file.");

        foreach (var group in CommandCatalog.Groups)
        {
            Console.WriteLine();
            Write(group.ToUpperInvariant(), Theme.Violet, useColor, bold: true);
            Console.WriteLine();
            foreach (var entry in CommandCatalog.InGroup(group))
            {
                // A name wider than the column takes the row to itself, and its summary follows
                // on the next line indented to where every other summary starts. Padding to a
                // fixed width does nothing when the name is already wider than it, so
                // "integration install-claude-statusline" ran straight into its own description -
                // on the first page anybody reads.
                var summary = Text.Truncate(entry.Summary, Math.Max(20, width - NameColumn - 2));
                if (Text.DisplayWidth(entry.Name) >= NameColumn)
                {
                    Write($"  {entry.Name}", Theme.Cyan, useColor);
                    Console.WriteLine();
                    Console.WriteLine(new string(' ', NameColumn + 2) + summary);
                    continue;
                }

                Write("  " + entry.Name + new string(' ', NameColumn - Text.DisplayWidth(entry.Name)), Theme.Cyan, useColor);
                Console.WriteLine(summary);
            }
        }

        Console.WriteLine();
        Console.WriteLine("Read one in full with: fknrtd help <command>");
        Console.WriteLine();
        Write("COMMON OPTIONS", Theme.Violet, useColor, bold: true);
        Console.WriteLine();
        Console.WriteLine("  -root <path>    Act on the workspace at this path instead of the current folder");
        Console.WriteLine("  -json           Emit machine-readable JSON where supported");
        Console.WriteLine("  -no-color       Disable ANSI colour");
        Console.WriteLine("  -color          Force ANSI colour even when output is redirected");
        Console.WriteLine();
        Write("EXIT CODES", Theme.Violet, useColor, bold: true);
        Console.WriteLine();
        Console.WriteLine("  0  success");
        Console.WriteLine("  1  an error was raised — read the diagnostic; bad configuration, a");
        Console.WriteLine("     missing task or a refused confirmation all land here");
        Console.WriteLine("  2  unknown command, or a required pre-flight check failed");
        Console.WriteLine("  3  the operation ran and its outcome was a failure or a collision");
        Console.WriteLine("  130  cancelled");
    }

    private static void Group(string group, int width, bool useColor)
    {
        Write(group.ToUpperInvariant(), Theme.Violet, useColor, bold: true);
        Console.WriteLine();
        foreach (var entry in CommandCatalog.InGroup(group))
        {
            Console.WriteLine();
            Write("  " + entry.Invocation, Theme.Cyan, useColor, bold: true);
            Console.WriteLine();
            foreach (var line in Text.Wrap(entry.Summary, width - 4))
            {
                Console.WriteLine("    " + line);
            }
        }
    }

    private static void Detail(CommandEntry entry, int width, bool useColor)
    {
        Write(entry.Invocation, Theme.Cyan, useColor, bold: true);
        Console.WriteLine();
        Write(entry.Group, Theme.Muted, useColor);
        Console.WriteLine();
        Console.WriteLine();

        foreach (var line in Text.Wrap(entry.Summary, width))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
        foreach (var line in Text.Wrap(entry.Detail, width))
        {
            Console.WriteLine(line);
        }

        if (entry.Options.Count > 0)
        {
            Console.WriteLine();
            Write("OPTIONS", Theme.Violet, useColor, bold: true);
            Console.WriteLine();
            var labelWidth = Math.Min(28, entry.Options.Max(option =>
                option.Name.Length + option.ValueHint.Length + 3));
            foreach (var option in entry.Options)
            {
                var label = option.ValueHint.Length == 0
                    ? option.Name
                    : $"{option.Name} {option.ValueHint}";
                Write("  " + label.PadRight(labelWidth), Theme.Cyan, useColor);
                var meaning = option.Required ? option.Meaning + "  (required)" : option.Meaning;
                var wrapped = Text.Wrap(meaning, Math.Max(20, width - labelWidth - 3));
                Console.WriteLine(wrapped.FirstOrDefault());
                foreach (var continuation in wrapped.Skip(1))
                {
                    Console.WriteLine(new string(' ', labelWidth + 2) + continuation);
                }
            }
        }

        if (entry.Examples.Count > 0)
        {
            Console.WriteLine();
            Write("EXAMPLES", Theme.Violet, useColor, bold: true);
            Console.WriteLine();
            foreach (var example in entry.Examples)
            {
                Console.WriteLine("  " + example);
            }
        }

        Console.WriteLine();
        Write("WHAT HAPPENS NEXT", Theme.Green, useColor, bold: true);
        Console.WriteLine();
        foreach (var line in Text.Wrap(entry.WhatHappensNext, width - 2))
        {
            Console.WriteLine("  " + line);
        }

        var terms = entry.GlossaryTerms.Select(Glossary.Find).OfType<GlossaryEntry>().ToArray();
        if (terms.Length > 0)
        {
            Console.WriteLine();
            Write("WORDS USED HERE", Theme.Violet, useColor, bold: true);
            Console.WriteLine();
            foreach (var term in terms)
            {
                Write($"  {term.Term,-20}", Theme.Cyan, useColor);
                Console.WriteLine(Text.Truncate(term.Summary, Math.Max(20, width - 22)));
            }

            Console.WriteLine();
            Console.WriteLine($"  Read one with: fknrtd explain {terms[0].Term}");
        }
    }

    /// <summary>The message for a command that does not exist, with the nearest ones that do.</summary>
    /// <summary>
    /// Reject an option the command does not accept, naming the nearest one it does.
    /// </summary>
    /// <remarks>
    /// A mistyped command has always been caught. A mistyped option was not: every command reads
    /// the options it knows and nothing looked at the remainder, so <c>fknrtd task list -jsno</c>
    /// printed a human table and exited 0. The exit code is the damage — a script that asked for
    /// JSON was told it succeeded — so this exits 2, the same as a mistyped command.
    /// </remarks>
    public static int UnknownOption(string command, string option, CommandEntry entry)
    {
        Console.Error.WriteLine($"FKNRTD.CLI error: 'fknrtd {command}' has no -{option} option.");

        var accepted = entry.Options
            .Where(candidate => candidate.Name.StartsWith('-'))
            .ToArray();
        var near = NearOptions(option, accepted);

        if (near.Length > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(near.Length == 1 ? "Did you mean:" : "The closest options are:");
            foreach (var candidate in near.Take(3))
            {
                Console.Error.WriteLine($"  {candidate.Name + " " + candidate.ValueHint,-22} {candidate.Meaning}");
            }
        }
        else if (accepted.Length > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"It accepts: {string.Join(", ", accepted.Select(candidate => candidate.Name))}");
        }
        else
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("It accepts no options.");
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Run 'fknrtd help {command}' for what each one does.");
        return 2;
    }

    public static int Unknown(string command)
    {
        Console.Error.WriteLine($"There is no '{command}' command in FKNRTD.CLI.");
        var suggestions = Nearest(command);
        if (suggestions.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("The closest matches are:");
            foreach (var candidate in suggestions.Take(4))
            {
                Console.Error.WriteLine($"  fknrtd {candidate.Name,-24} {candidate.Summary}");
            }
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("Run 'fknrtd help' for every command, or 'fknrtd explain' for every term.");
        return 2;
    }

    /// <summary>
    /// Commands close to what was typed. Substring search alone cannot help a typo — nothing
    /// contains "taks" — so the nearest names by edit distance are offered first, which is the case
    /// an unknown-command message exists to serve.
    /// </summary>
    internal static IReadOnlyList<CommandEntry> Nearest(string command)
    {
        var needle = command.Trim();
        if (needle.Length == 0)
        {
            return [];
        }

        // Two edits covers a transposition, a doubled letter or a dropped one; more than that and a
        // guess is noise. Short names get a tighter budget so "run" does not match "land".
        var budget = needle.Length <= 4 ? 1 : 2;
        var byDistance = CommandCatalog.All
            .Select(entry => (entry, distance: Distance(needle, entry.Name.Split(' ')[0])))
            .Where(item => item.distance <= budget)
            .OrderBy(item => item.distance)
            .ThenBy(item => item.entry.Name.Length)
            .Select(item => item.entry)
            .ToArray();

        return byDistance.Length > 0 ? byDistance : CommandCatalog.Search(needle);
    }

    /// <summary>
    /// Damerau-Levenshtein distance, case-insensitive. Transposition counts as one edit rather than
    /// two, because swapping two letters is the typo people actually make: without it "taks" is as
    /// far from "task" as it is from nothing, and the suggestion that would have helped is dropped.
    /// Command names are a handful of characters, so a plain matrix is the clearest thing that works.
    /// </summary>
    /// <summary>
    /// The accepted options closest to one a command refused, best first, or none.
    /// </summary>
    /// <remarks>
    /// An abbreviation is not a typo, and edit distance scores one worst exactly when it is
    /// shortest and most deliberate: <c>-y</c> for <c>-yes</c> is two insertions, which no
    /// threshold that still rejects noise can admit. So the most universal abbreviation in the
    /// language got the least help — six options listed, and the reader left to notice that one of
    /// them was the word they had already typed the first letter of. A prefix is matched on its own
    /// terms and ranks ahead of a spelling near-miss, being the more confident signal: somebody
    /// writing <c>-q</c> knows which option they want.
    ///
    /// A prefix is still refused rather than accepted. Accepting one would mean that adding an
    /// option later silently changed what an existing script's <c>-y</c> referred to, and this
    /// check exists because an option that is read wrongly and reported as success is the damage.
    /// </remarks>
    internal static CommandOption[] NearOptions(string option, IReadOnlyList<CommandOption> accepted)
    {
        return accepted
            .Select(candidate => (candidate, rank: Rank(option, candidate.Name.TrimStart('-'))))
            .Where(item => item.rank >= 0)
            .OrderBy(item => item.rank)
            .ThenBy(item => item.candidate.Name.Length)
            .Select(item => item.candidate)
            .ToArray();

        // Below zero is "not close enough to offer". A prefix sorts first, then a near spelling by
        // how near it is. The threshold stays tighter for a short option because at two or three
        // characters almost everything is within two edits of almost everything else.
        static int Rank(string typed, string name)
        {
            if (typed.Length > 0 && name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            var distance = Distance(typed, name);
            return distance <= (typed.Length <= 4 ? 1 : 2) ? distance : -1;
        }
    }

    internal static int Distance(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return Math.Max(left.Length, right.Length);
        }

        var distance = new int[left.Length + 1, right.Length + 1];
        for (var row = 0; row <= left.Length; row++)
        {
            distance[row, 0] = row;
        }

        for (var column = 0; column <= right.Length; column++)
        {
            distance[0, column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = char.ToLowerInvariant(left[row - 1]) == char.ToLowerInvariant(right[column - 1])
                    ? 0
                    : 1;
                distance[row, column] = Math.Min(
                    Math.Min(distance[row, column - 1] + 1, distance[row - 1, column] + 1),
                    distance[row - 1, column - 1] + substitution);

                if (row > 1 && column > 1 &&
                    char.ToLowerInvariant(left[row - 1]) == char.ToLowerInvariant(right[column - 2]) &&
                    char.ToLowerInvariant(left[row - 2]) == char.ToLowerInvariant(right[column - 1]))
                {
                    distance[row, column] = Math.Min(distance[row, column], distance[row - 2, column - 2] + 1);
                }
            }
        }

        return distance[left.Length, right.Length];
    }

    private static void Write(string text, Rgb colour, bool useColor, bool bold = false)
    {
        if (!useColor)
        {
            Console.Write(text);
            return;
        }

        Console.Write(colour.ForegroundCode);
        if (bold)
        {
            Console.Write("\u001b[1m");
        }

        Console.Write(text);
        Console.Write("\u001b[0m");
    }
}
