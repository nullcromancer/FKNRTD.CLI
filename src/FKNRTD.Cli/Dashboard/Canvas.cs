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

    public void DrawText(int x, int y, string text, Rgb color, bool bold = false, int? maxWidth = null)
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
            SetGlyph(cursor, y, printable, displayWidth, color, bold);
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
                                 previous.Value.Bold != cell.Bold))
                {
                    output.Append("\u001b[0m");
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

    private void SetGlyph(int x, int y, string content, int displayWidth, Rgb color, bool bold)
    {
        for (var column = x; column < x + displayWidth; column++)
        {
            ClearGlyphAt(column, y);
        }

        _cells[y, x] = new Cell(content, displayWidth, color, bold);
        for (var column = x + 1; column < x + displayWidth; column++)
        {
            _cells[y, column] = new Cell(string.Empty, 0, color, bold);
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
            _cells[y, column] = Cell.Blank;
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

    private readonly record struct Cell(string Content, int DisplayWidth, Rgb Color, bool Bold)
    {
        public static Cell Blank => new(" ", 1, Theme.Foreground, false);
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

    public static string Age(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var age = now - timestamp;
        if (age.TotalSeconds < 60)
        {
            return $"{Math.Max(0, age.TotalSeconds):0}s";
        }

        if (age.TotalMinutes < 60)
        {
            return $"{age.TotalMinutes:0}m";
        }

        if (age.TotalHours < 24)
        {
            return $"{age.TotalHours:0}h";
        }

        return $"{age.TotalDays:0}d";
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
