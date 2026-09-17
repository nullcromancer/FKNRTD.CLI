using FKNRTD.Dashboard;
using FKNRTD.Help;

namespace FKNRTD.Commands;

/// <summary>
/// <c>fknrtd explain</c>. The glossary, on the command line, for the operator who met a word in an
/// error message or a colleague's shell history rather than in the dashboard. It reads the same
/// table the dashboard's inline hints read, so the two can never say different things.
/// </summary>
internal static class ExplainCommand
{
    public static int Execute(CliArguments arguments, bool useColor)
    {
        var term = arguments.Positional(1);
        var width = Math.Clamp(Screen.Width(88) - 2, 40, 96);

        if (string.IsNullOrWhiteSpace(term))
        {
            return List(width, useColor);
        }

        var entry = Glossary.Find(term);
        if (entry is not null)
        {
            Print(entry, width, useColor);
            return 0;
        }

        // A miss is far more useful as a search than as a refusal: the word the operator typed is
        // usually a word that appears inside the entry they are looking for.
        var matches = Glossary.Search(term);
        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"Nothing in the FKNRTD.CLI glossary matches '{term}'.");
            Console.Error.WriteLine("Run 'fknrtd explain' with no argument to see every term.");
            return 2;
        }

        Console.WriteLine($"No term is called '{term}'. These mention it:");
        Console.WriteLine();
        foreach (var match in matches.Take(8))
        {
            Write($"  {match.Term,-22}", Theme.Cyan, useColor);
            Console.WriteLine(Text.Truncate(match.Summary, Math.Max(20, width - 24)));
        }

        Console.WriteLine();
        Console.WriteLine($"Read one with: fknrtd explain {matches[0].Term}");
        return 0;
    }

    private static int List(int width, bool useColor)
    {
        Write("FKNRTD.CLI GLOSSARY", Theme.Cyan, useColor, bold: true);
        Console.WriteLine();
        Console.WriteLine("Every word this product uses, and what it means. Read one in full with:");
        Console.WriteLine("  fknrtd explain <term>");

        foreach (var category in Glossary.Categories)
        {
            Console.WriteLine();
            Write(category.ToUpperInvariant(), Theme.Violet, useColor, bold: true);
            Console.WriteLine();
            foreach (var entry in Glossary.InCategory(category))
            {
                Write($"  {entry.Term,-22}", Theme.Cyan, useColor);
                Console.WriteLine(Text.Truncate(entry.Summary, Math.Max(20, width - 24)));
            }
        }

        return 0;
    }

    private static void Print(GlossaryEntry entry, int width, bool useColor)
    {
        Write(entry.Title.ToUpperInvariant(), Theme.Cyan, useColor, bold: true);
        Console.WriteLine();
        Write($"{entry.Category} · {entry.Term}", Theme.Muted, useColor);
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

        if (entry.Example.Length > 0)
        {
            Console.WriteLine();
            Write("EXAMPLE", Theme.Violet, useColor, bold: true);
            Console.WriteLine();
            foreach (var line in Text.Wrap(entry.Example, width - 2))
            {
                Console.WriteLine("  " + line);
            }
        }

        var related = Related(entry);
        if (related.Count > 0)
        {
            Console.WriteLine();
            Write("SEE ALSO", Theme.Violet, useColor, bold: true);
            Console.WriteLine();
            Console.WriteLine("  " + string.Join("  ", related.Select(item => item.Term)));
        }
    }

    /// <summary>
    /// Other entries in the same family or category. Related terms are how someone who looked up one
    /// word finds the two more they actually needed.
    /// </summary>
    private static IReadOnlyList<GlossaryEntry> Related(GlossaryEntry entry)
    {
        var family = entry.Term.Contains('.', StringComparison.Ordinal)
            ? entry.Term[..entry.Term.IndexOf('.', StringComparison.Ordinal)]
            : null;
        return Glossary.All
            .Where(other => other.Term != entry.Term)
            .Where(other => family is not null
                ? other.Term.StartsWith(family + ".", StringComparison.Ordinal)
                : other.Category == entry.Category)
            .Take(8)
            .ToArray();
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
            Console.Write("[1m");
        }

        Console.Write(text);
        Console.Write("[0m");
    }
}
