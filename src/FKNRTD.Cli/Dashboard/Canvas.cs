using System.Globalization;
using System.Text;

namespace FKNRTD.Dashboard;

internal sealed class Canvas
{
    private readonly Cell[,] _cells;

    public Canvas(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _cells = new Cell[Height, Width];
        Clear();
    }

    public int Width { get; }
    public int Height { get; }

    public void Clear()
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                _cells[y, x] = Cell.Blank;
            }
        }
    }

    public void DrawText(int x, int y, string text, Rgb color, bool bold = false, int? maxWidth = null,
        Rgb? background = null)
    {
        if (y < 0 || y >= Height || x >= Width)
        {
            return;
        }

        var remaining = Math.Max(0, maxWidth ?? int.MaxValue);
        var cursor = x;
        foreach (var element in Text.Elements(text))
        {
            var displayWidth = Text.DisplayWidth(element);
            if (displayWidth == 0)
            {
                continue;
            }

            if (displayWidth > remaining)
            {
                break;
            }

            if (cursor < 0)
            {
                if (cursor + displayWidth > 0)
                {
                    break;
                }

                cursor += displayWidth;
                remaining -= displayWidth;
                continue;
            }

            if (cursor + displayWidth > Width)
            {
                break;
            }

            var printable = element.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
                ? " "
                : element;
            SetGlyph(cursor, y, printable, displayWidth, color, bold, background);
            cursor += displayWidth;
            remaining -= displayWidth;
        }
    }

    public void DrawBox(Rect rect, string title, Rgb color)
    {
        if (rect.Width < 2 || rect.Height < 2)
        {
            return;
        }

        SetBox(rect.X, rect.Y, BoxLine.Right | BoxLine.Down, color);
        SetBox(rect.Right - 1, rect.Y, BoxLine.Left | BoxLine.Down, color);
        SetBox(rect.X, rect.Bottom - 1, BoxLine.Right | BoxLine.Up, color);
        SetBox(rect.Right - 1, rect.Bottom - 1, BoxLine.Left | BoxLine.Up, color);
        for (var x = rect.X + 1; x < rect.Right - 1; x++)
        {
            SetBox(x, rect.Y, BoxLine.Left | BoxLine.Right, color);
            SetBox(x, rect.Bottom - 1, BoxLine.Left | BoxLine.Right, color);
        }

        for (var y = rect.Y + 1; y < rect.Bottom - 1; y++)
        {
            SetBox(rect.X, y, BoxLine.Up | BoxLine.Down, color);
            SetBox(rect.Right - 1, y, BoxLine.Up | BoxLine.Down, color);
        }

        if (!string.IsNullOrWhiteSpace(title) && rect.Width > 6)
        {
            var label = " " + Text.Truncate(title, rect.Width - 5) + " ";
            DrawText(rect.X + 2, rect.Y, label, color, bold: true, maxWidth: rect.Width - 4);
        }
    }

    public void DrawGauge(int x, int y, int width, double? percent, Rgb color, bool showPercent = true)
    {
        if (width <= 0)
        {
            return;
        }

        if (percent is null)
        {
            DrawText(x, y, "N/A", Theme.Muted, maxWidth: width);
            return;
        }

        var value = Math.Clamp(percent.Value, 0, 100);
        var suffix = showPercent ? $" {value:0}%" : string.Empty;
        var suffixWidth = Text.DisplayWidth(suffix);
        var barWidth = Math.Max(1, width - suffixWidth);
        var filled = (int)Math.Round(barWidth * value / 100, MidpointRounding.AwayFromZero);
        DrawText(x, y, new string('█', filled), color, maxWidth: barWidth);
        DrawText(x + filled, y, new string('░', barWidth - filled), Theme.Muted, maxWidth: barWidth - filled);
        if (showPercent)
        {
            DrawText(x + barWidth, y, suffix, color, maxWidth: suffixWidth);
        }
    }

    /// <summary>Paints a background over a region, clearing the glyphs underneath it.</summary>
    public void Fill(Rect rect, Rgb background)
    {
        for (var y = Math.Max(0, rect.Y); y < Math.Min(Height, rect.Bottom); y++)
        {
            for (var x = Math.Max(0, rect.X); x < Math.Min(Width, rect.Right); x++)
            {
                SetGlyph(x, y, " ", 1, Theme.Foreground, false, background);
            }
        }
    }

    /// <summary>
    /// Draws a modal panel: a filled region inside a heavy border. The dashboard's own sections use
    /// light box-drawing, so the heavier weight is what makes a panel read as a layer above them
    /// rather than as one more section competing for attention.
    /// </summary>
    public void DrawPanel(Rect rect, string title, Rgb accent, Rgb background)
    {
        if (rect.Width < 4 || rect.Height < 3)
        {
            return;
        }

        Fill(rect, background);
        var right = rect.Right - 1;
        var bottom = rect.Bottom - 1;
        DrawText(rect.X, rect.Y, "┏" + new string('━', rect.Width - 2) + "┓", accent, maxWidth: rect.Width,
            background: background);
        DrawText(rect.X, bottom, "┗" + new string('━', rect.Width - 2) + "┛", accent, maxWidth: rect.Width,
            background: background);
        for (var y = rect.Y + 1; y < bottom; y++)
        {
            DrawText(rect.X, y, "┃", accent, maxWidth: 1, background: background);
            DrawText(right, y, "┃", accent, maxWidth: 1, background: background);
        }

        if (!string.IsNullOrWhiteSpace(title) && rect.Width > 8)
        {
            DrawText(rect.X + 2, rect.Y, " " + Text.Truncate(title, rect.Width - 8) + " ", accent, bold: true,
                maxWidth: rect.Width - 4, background: background);
        }
    }

    /// <summary>
    /// Recolours a region without changing its glyphs. A modal draws this over the whole frame
    /// first, so the dashboard stays legible behind the question instead of being replaced by it —
    /// the operator can still see the task they are acting on while they answer.
    /// </summary>
    public void Dim(Rect rect, Rgb foreground, Rgb background)
    {
        for (var y = Math.Max(0, rect.Y); y < Math.Min(Height, rect.Bottom); y++)
        {
            for (var x = Math.Max(0, rect.X); x < Math.Min(Width, rect.Right); x++)
            {
                var cell = _cells[y, x];
                _cells[y, x] = cell with { Color = foreground, Bold = false, Background = background };
            }
        }
    }

    /// <summary>A horizontal divider inside a panel.</summary>
    public void DrawRule(int x, int y, int width, Rgb color, Rgb? background = null) =>
        DrawText(x, y, new string('─', Math.Max(0, width)), color, maxWidth: width, background: background);

    /// <summary>
    /// Draws word-wrapped text and returns the row after the last one written, so callers can stack
    /// paragraphs without tracking heights themselves.
    /// </summary>
    public int DrawWrapped(
        int x,
        int y,
        int width,
        int maxRows,
        string text,
        Rgb color,
        bool bold = false,
        Rgb? background = null)
    {
        var row = y;
        foreach (var line in Text.Wrap(text, width))
        {
            if (row >= y + maxRows)
            {
                break;
            }

            DrawText(x, row++, line, color, bold, width, background);
        }

        return row;
    }

    public string Render(bool useColor)
    {
        var output = new StringBuilder(Width * Height * 2);
        Cell? previous = null;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var cell = _cells[y, x];
                if (cell.DisplayWidth == 0)
                {
                    continue;
                }

                if (useColor && (previous is null || previous.Value.Color != cell.Color ||
                                 previous.Value.Bold != cell.Bold ||
                                 previous.Value.Background != cell.Background))
                {
                    output.Append("\u001b[0m");
                    if (cell.Background is { } fill)
                    {
                        output.Append(fill.BackgroundCode);
                    }

                    output.Append(cell.Color.ForegroundCode);
                    if (cell.Bold)
                    {
                        output.Append("\u001b[1m");
                    }
                }

                output.Append(cell.Content);
                previous = cell;
            }

            if (useColor)
            {
                output.Append("\u001b[0m");
                previous = null;
            }

            if (y < Height - 1)
            {
                output.AppendLine();
            }
        }

        return output.ToString();
    }

    private void SetBox(int x, int y, BoxLine lines, Rgb color)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        var merged = Connections(_cells[y, x].Content) | lines;
        SetGlyph(x, y, BoxCharacter(merged).ToString(), 1, color, false);
    }

    private void SetGlyph(int x, int y, string content, int displayWidth, Rgb color, bool bold,
        Rgb? background = null)
    {
        // A glyph drawn without an explicit background keeps whatever fill is already beneath it,
        // so text drawn over a filled panel does not punch holes in the panel.
        var fill = background ?? _cells[y, x].Background;
        for (var column = x; column < x + displayWidth; column++)
        {
            ClearGlyphAt(column, y);
        }

        _cells[y, x] = new Cell(content, displayWidth, color, bold, fill);
        for (var column = x + 1; column < x + displayWidth; column++)
        {
            _cells[y, column] = new Cell(string.Empty, 0, color, bold, fill);
        }
    }

    private void ClearGlyphAt(int x, int y)
    {
        var start = x;
        while (start > 0 && _cells[y, start].DisplayWidth == 0)
        {
            start--;
        }

        var width = Math.Max(1, _cells[y, start].DisplayWidth);
        for (var column = start; column < Math.Min(Width, start + width); column++)
        {
            _cells[y, column] = Cell.Blank with { Background = _cells[y, column].Background };
        }
    }

    private static BoxLine Connections(string content) => content switch
    {
        "─" => BoxLine.Left | BoxLine.Right,
        "│" => BoxLine.Up | BoxLine.Down,
        "┌" => BoxLine.Right | BoxLine.Down,
        "┐" => BoxLine.Left | BoxLine.Down,
        "└" => BoxLine.Right | BoxLine.Up,
        "┘" => BoxLine.Left | BoxLine.Up,
        "┬" => BoxLine.Left | BoxLine.Right | BoxLine.Down,
        "┴" => BoxLine.Left | BoxLine.Right | BoxLine.Up,
        "├" => BoxLine.Up | BoxLine.Right | BoxLine.Down,
        "┤" => BoxLine.Up | BoxLine.Left | BoxLine.Down,
        "┼" => BoxLine.Up | BoxLine.Right | BoxLine.Down | BoxLine.Left,
        _ => BoxLine.None
    };

    private static char BoxCharacter(BoxLine lines) => lines switch
    {
        BoxLine.Left | BoxLine.Right => '─',
        BoxLine.Up | BoxLine.Down => '│',
        BoxLine.Right | BoxLine.Down => '┌',
        BoxLine.Left | BoxLine.Down => '┐',
        BoxLine.Right | BoxLine.Up => '└',
        BoxLine.Left | BoxLine.Up => '┘',
        BoxLine.Left | BoxLine.Right | BoxLine.Down => '┬',
        BoxLine.Left | BoxLine.Right | BoxLine.Up => '┴',
        BoxLine.Up | BoxLine.Right | BoxLine.Down => '├',
        BoxLine.Up | BoxLine.Left | BoxLine.Down => '┤',
        BoxLine.Up | BoxLine.Right | BoxLine.Down | BoxLine.Left => '┼',
        _ => ' '
    };

    [Flags]
    private enum BoxLine
    {
        None = 0,
        Up = 1,
        Right = 2,
        Down = 4,
        Left = 8
    }

    private readonly record struct Cell(string Content, int DisplayWidth, Rgb Color, bool Bold, Rgb? Background)
    {
        public static Cell Blank => new(" ", 1, Theme.Foreground, false, null);
    }
}

internal readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public Rect Inset(int horizontal = 1, int vertical = 1) =>
        new(X + horizontal, Y + vertical, Math.Max(0, Width - horizontal * 2), Math.Max(0, Height - vertical * 2));
}

internal static class Text
{
    public static string Truncate(string? value, int maxWidth)
    {
        if (string.IsNullOrEmpty(value) || maxWidth <= 0)
        {
            return string.Empty;
        }

        var clean = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        if (DisplayWidth(clean) <= maxWidth)
        {
            return clean;
        }

        if (maxWidth == 1)
        {
            return "…";
        }

        var output = new StringBuilder();
        var remaining = maxWidth - 1;
        foreach (var element in Elements(clean))
        {
            var width = DisplayWidth(element);
            if (width > remaining)
            {
                break;
            }

            output.Append(element);
            remaining -= width;
        }

        return output.Append('…').ToString();
    }

    /// <summary>
    /// Breaks text into lines that fit <paramref name="width"/> display columns, splitting on spaces
    /// and honouring explicit newlines. A single word longer than the width is split rather than
    /// allowed to overflow the panel it is being drawn into.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string? value, int width)
    {
        if (string.IsNullOrEmpty(value) || width <= 0)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var paragraph in value.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var line = new StringBuilder();
            var lineWidth = 0;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var wordWidth = DisplayWidth(word);
                if (wordWidth > width)
                {
                    if (lineWidth > 0)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                        lineWidth = 0;
                    }

                    foreach (var piece in Split(word, width))
                    {
                        lines.Add(piece);
                    }

                    continue;
                }

                var separator = lineWidth == 0 ? 0 : 1;
                if (lineWidth + separator + wordWidth > width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                    lineWidth = 0;
                    separator = 0;
                }

                if (separator == 1)
                {
                    line.Append(' ');
                }

                line.Append(word);
                lineWidth += separator + wordWidth;
            }

            if (lineWidth > 0)
            {
                lines.Add(line.ToString());
            }
        }

        return lines;
    }

    /// <summary>Hard-splits a word too long to wrap, never breaking a grapheme cluster.</summary>
    /// <summary>
    /// Breaks a word too long for the line into pieces, at a separator inside it where there is a
    /// usable one.
    /// </summary>
    /// <remarks>
    /// A blind break at the column gives the worst possible result for the thing most likely to be
    /// too long, which is a path: doctor reported its config file as
    /// "...\.fknrtd\config.jso" followed by a line holding the single letter "n". Breaking after
    /// a separator keeps each piece a readable fragment of the path and the last one a whole
    /// filename. Only a break that leaves a substantial first piece is worth taking - otherwise
    /// the pieces get so short that the word takes more lines than it needs.
    /// </remarks>
    private static IEnumerable<string> Split(string word, int width)
    {
        var remaining = word;
        while (DisplayWidth(remaining) > width)
        {
            var head = Take(remaining, width);

            // A path separator is preferred over any other, so that a filename stays whole: at the
            // dot instead, the config file above broke into "config." and "json".
            var breakAt = head.LastIndexOfAny(PathSeparators);
            if (breakAt < width / 2)
            {
                breakAt = head.LastIndexOfAny(SeparatorCharacters);
            }

            // Past the halfway mark, so that a separator near the start of a long path does not
            // produce a column of stubs.
            if (breakAt >= width / 2 && breakAt < head.Length - 1)
            {
                head = head[..(breakAt + 1)];
            }

            yield return head;
            remaining = remaining[head.Length..];
        }

        if (remaining.Length > 0)
        {
            yield return remaining;
        }
    }

    /// <summary>Preferred break points: a piece of a path reads as a piece of a path.</summary>
    private static readonly char[] PathSeparators = ['\\', '/'];

    /// <summary>Where else a word may be broken when it is too long to fit on one line.</summary>
    private static readonly char[] SeparatorCharacters = ['\\', '/', '-', '_', '.', ',', ';', ':'];

    /// <summary>As many whole elements of <paramref name="word"/> as fit in <paramref name="width"/>.</summary>
    private static string Take(string word, int width)
    {
        var piece = new StringBuilder();
        var pieceWidth = 0;
        foreach (var element in Elements(word))
        {
            var elementWidth = DisplayWidth(element);
            if (pieceWidth + elementWidth > width)
            {
                break;
            }

            piece.Append(element);
            pieceWidth += elementWidth;
        }

        // A single element wider than the whole line still has to make progress, or this loops.
        return piece.Length > 0 ? piece.ToString() : Elements(word).First();
    }

    public static int DisplayWidth(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var total = 0;
        foreach (var element in Elements(value))
        {
            var elementWidth = 0;
            var emojiPresentation = false;
            foreach (var rune in element.EnumerateRunes())
            {
                elementWidth = Math.Max(elementWidth, RuneWidth(rune));
                emojiPresentation |= rune.Value is 0xfe0f or 0x20e3;
            }

            total += emojiPresentation ? Math.Max(2, elementWidth) : elementWidth;
        }

        return total;
    }

    public static IEnumerable<string> Elements(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            yield return enumerator.GetTextElement();
        }
    }

    /// <summary>
    /// How long ago something happened, in the largest unit that still has a whole number in it.
    /// </summary>
    /// <remarks>
    /// Truncated rather than rounded, for two reasons. Rounding reaches values nobody writes: at
    /// 59.6 seconds it said "60s", at 59.5 minutes "60m", and at 23.9 hours "24h" — each of which
    /// reads as a bug rather than as a time. And rounding up makes things look older than they are:
    /// something ninety seconds old was reported as "2m ago". An age is a floor, the way an age in
    /// years is.
    ///
    /// A timestamp in the future — clock skew, or a record written by another machine — reads as
    /// "0s" rather than as a negative number.
    /// </remarks>
    public static string Age(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var seconds = (long)Math.Max(0, (now - timestamp).TotalSeconds);
        if (seconds < 60)
        {
            return seconds + "s";
        }

        if (seconds < 3600)
        {
            return seconds / 60 + "m";
        }

        return seconds < 86400 ? seconds / 3600 + "h" : seconds / 86400 + "d";
    }

    private static int RuneWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
        {
            return 0;
        }

        var value = rune.Value;
        // BCL exposes Unicode categories but not East Asian Width. Cover Hangul Jamo,
        // CJK/kana/fullwidth blocks, emoji pictographs, and supplementary ideographs.
        return value is >= 0x1100 and <= 0x115f or
            0x2329 or 0x232a or
            >= 0x2e80 and <= 0x303e or
            >= 0x3040 and <= 0xa4cf or
            >= 0xac00 and <= 0xd7a3 or
            >= 0xf900 and <= 0xfaff or
            >= 0xfe10 and <= 0xfe19 or
            >= 0xfe30 and <= 0xfe6f or
            >= 0xff00 and <= 0xff60 or
            >= 0xffe0 and <= 0xffe6 or
            >= 0x1f000 and <= 0x1faff or
            >= 0x20000 and <= 0x3fffd
            ? 2
            : 1;
    }
}
