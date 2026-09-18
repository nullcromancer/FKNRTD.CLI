using FKNRTD.Dashboard;

namespace FKNRTD.Commands;

/// <summary>
/// One row of a two-column reference listing: a name on the left, what it means on the right.
/// </summary>
/// <remarks>
/// Every listing this product prints - commands, glossary terms, settings, the words a command
/// uses - is this same shape, and each one wrote its own <c>{name,-N}</c>. Padding to a fixed
/// width does nothing to a name already that wide, so the name and its description ran together:
/// <c>fknrtd help</c> printed "integration install-claude-statuslineConnect Claude Code's..." on
/// the first page anybody reads. Every listing shares this now, so the fault cannot be
/// reintroduced one table at a time, and a name is added to a catalog by writing a row - nothing
/// about that row says how wide the column is.
/// </remarks>
internal static class NamedRow
{
    /// <summary>
    /// Writes the row, putting the description on its own indented line when the name is too wide
    /// to share one with it.
    /// </summary>
    /// <param name="name">The left column: a command, a term, a setting key.</param>
    /// <param name="description">The right column, truncated to the window.</param>
    /// <param name="nameColumn">Columns reserved for the name, not counting the two-space indent.</param>
    /// <param name="width">The window width.</param>
    /// <param name="colour">The name's colour.</param>
    /// <param name="useColor">Whether to emit colour at all.</param>
    /// <param name="write">How to write the coloured name; each command has its own.</param>
    public static void Write(
        string name,
        string description,
        int nameColumn,
        int width,
        Rgb colour,
        bool useColor,
        Action<string, Rgb, bool, bool> write)
    {
        var text = Text.Truncate(description, Math.Max(20, width - nameColumn - 2));
        var nameWidth = Text.DisplayWidth(name);

        if (nameWidth >= nameColumn)
        {
            write("  " + name, colour, useColor, false);
            Console.WriteLine();
            Console.WriteLine(new string(' ', nameColumn + 2) + text);
            return;
        }

        write("  " + name + new string(' ', nameColumn - nameWidth), colour, useColor, false);
        Console.WriteLine(text);
    }
}
