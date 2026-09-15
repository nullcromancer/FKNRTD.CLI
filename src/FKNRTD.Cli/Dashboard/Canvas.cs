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
                _cells[y, x] = new Cell(' ', Theme.Foreground, false);
            }
        }
    }

    public void DrawText(int x, int y, string text, Rgb color, bool bold = false, int? maxWidth = null)
    {
        if (y < 0 || y >= Height || x >= Width)
        {
            return;
        }

        var remaining = Math.Min(maxWidth ?? int.MaxValue, Width - Math.Max(0, x));
        var cursor = x;
        foreach (var character in text)
        {
            if (remaining <= 0 || cursor >= Width)
            {
                break;
            }

            if (cursor >= 0)
            {
                _cells[y, cursor] = new Cell(char.IsControl(character) ? ' ' : character, color, bold);
                remaining--;
            }

            cursor++;
        }
    }

    public void DrawBox(Rect rect, string title, Rgb color)
    {
        if (rect.Width < 2 || rect.Height < 2)
        {
            return;
        }

        Set(rect.X, rect.Y, '┌', color);
        Set(rect.Right - 1, rect.Y, '┐', color);
        Set(rect.X, rect.Bottom - 1, '└', color);
        Set(rect.Right - 1, rect.Bottom - 1, '┘', color);
        for (var x = rect.X + 1; x < rect.Right - 1; x++)
        {
            Set(x, rect.Y, '─', color);
            Set(x, rect.Bottom - 1, '─', color);
        }

        for (var y = rect.Y + 1; y < rect.Bottom - 1; y++)
        {
            Set(rect.X, y, '│', color);
            Set(rect.Right - 1, y, '│', color);
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
        var barWidth = Math.Max(1, width - suffix.Length);
        var filled = (int)Math.Round(barWidth * value / 100, MidpointRounding.AwayFromZero);
        DrawText(x, y, new string('█', filled), color, maxWidth: barWidth);
        DrawText(x + filled, y, new string('░', barWidth - filled), Theme.Muted, maxWidth: barWidth - filled);
        if (showPercent)
        {
            DrawText(x + barWidth, y, suffix, color, maxWidth: suffix.Length);
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

                output.Append(cell.Character);
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

    private void Set(int x, int y, char character, Rgb color, bool bold = false)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            _cells[y, x] = new Cell(character, color, bold);
        }
    }

    private readonly record struct Cell(char Character, Rgb Color, bool Bold);
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
        if (clean.Length <= maxWidth)
        {
            return clean;
        }

        return maxWidth <= 1 ? "…" : clean[..(maxWidth - 1)] + "…";
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
}
