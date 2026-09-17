namespace FKNRTD.Dashboard;

/// <summary>
/// An editable text field drawn onto the <see cref="Canvas"/>. It exists so that asking the
/// operator a question never means leaving the alternate screen for a bare
/// <see cref="Console.ReadLine"/>: the dashboard stays on screen behind the question, and the
/// question can carry its own explanation.
/// </summary>
internal sealed class TextField
{
    /// <summary>A visual line: a span of <see cref="Value"/> occupying one rendered row.</summary>
    internal readonly record struct Line(int Start, int Length)
    {
        public int End => Start + Length;
    }

    public TextField(string value = "", bool multiline = false)
    {
        Value = value ?? string.Empty;
        Multiline = multiline;
        Cursor = Value.Length;
    }

    public string Value { get; private set; }

    public bool Multiline { get; }

    public int Cursor { get; private set; }

    public bool IsEmpty => Value.Length == 0;

    public void Set(string value)
    {
        Value = value ?? string.Empty;
        Cursor = Value.Length;
    }

    /// <summary>
    /// Applies one keystroke. Returns false for keys the field does not own, so the surrounding
    /// form still sees Enter, Tab and Escape.
    /// </summary>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        var control = (key.Modifiers & ConsoleModifiers.Control) != 0;
        var alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                Cursor = control ? PreviousWord() : Step(-1);
                return true;
            case ConsoleKey.RightArrow:
                Cursor = control ? NextWord() : Step(1);
                return true;
            case ConsoleKey.Home:
                Cursor = 0;
                return true;
            case ConsoleKey.End:
                Cursor = Value.Length;
                return true;
            case ConsoleKey.Backspace:
                if (control)
                {
                    DeleteRange(PreviousWord(), Cursor);
                }
                else
                {
                    DeleteRange(Step(-1), Cursor);
                }

                return true;
            case ConsoleKey.Delete:
                DeleteRange(Cursor, Step(1));
                return true;
            case ConsoleKey.Enter:
                // Enter belongs to the form, so a multi-line field takes a modified Enter for a
                // paragraph break. Both spellings are accepted because terminals disagree on which
                // one they deliver.
                if (Multiline && (alt || control))
                {
                    Insert("\n");
                    return true;
                }

                return false;
        }

        if (control)
        {
            switch (key.Key)
            {
                case ConsoleKey.U:
                    DeleteRange(0, Cursor);
                    return true;
                case ConsoleKey.K:
                    DeleteRange(Cursor, Value.Length);
                    return true;
                case ConsoleKey.W:
                    DeleteRange(PreviousWord(), Cursor);
                    return true;
                case ConsoleKey.A:
                    Cursor = 0;
                    return true;
                case ConsoleKey.E:
                    Cursor = Value.Length;
                    return true;
                case ConsoleKey.J:
                    if (Multiline)
                    {
                        Insert("\n");
                        return true;
                    }

                    return false;
            }

            return false;
        }

        if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
        {
            Insert(key.KeyChar.ToString());
            return true;
        }

        return false;
    }

    /// <summary>
    /// Renders the field. A single-line field scrolls horizontally to keep the caret visible; a
    /// multi-line field wraps and scrolls vertically. Returns the number of rows used.
    /// </summary>
    public int Draw(Canvas canvas, Rect rect, bool focused, string placeholder, Rgb accent)
    {
        var fill = focused ? Theme.SurfaceRaised : Theme.SurfaceSunken;
        canvas.Fill(rect, fill);
        if (rect.Width <= 2 || rect.Height <= 0)
        {
            return 0;
        }

        var inner = new Rect(rect.X + 1, rect.Y, Math.Max(1, rect.Width - 2), rect.Height);
        if (Value.Length == 0 && !focused)
        {
            canvas.DrawText(inner.X, inner.Y, Text.Truncate(placeholder, inner.Width), Theme.Muted,
                maxWidth: inner.Width, background: fill);
            return 1;
        }

        return Multiline
            ? DrawWrapped(canvas, inner, focused, placeholder, fill, accent)
            : DrawSingle(canvas, inner, focused, placeholder, fill, accent);
    }

    private int DrawSingle(Canvas canvas, Rect inner, bool focused, string placeholder, Rgb fill, Rgb accent)
    {
        var caretColumn = Text.DisplayWidth(Value[..Cursor]);
        // Keep one column of slack past the caret so the block caret itself is never clipped.
        var scroll = Math.Max(0, caretColumn - inner.Width + 1);
        var visible = Slice(Value, scroll, inner.Width);
        if (Value.Length == 0)
        {
            canvas.DrawText(inner.X, inner.Y, Text.Truncate(placeholder, inner.Width), Theme.Muted,
                maxWidth: inner.Width, background: fill);
        }
        else
        {
            canvas.DrawText(inner.X, inner.Y, visible, Theme.Foreground, maxWidth: inner.Width, background: fill);
        }

        if (focused)
        {
            DrawCaret(canvas, inner.X + caretColumn - scroll, inner.Y, Under(placeholder), accent);
        }

        return 1;
    }

    private int DrawWrapped(Canvas canvas, Rect inner, bool focused, string placeholder, Rgb fill, Rgb accent)
    {
        var lines = Layout(Value, inner.Width);
        var caretLine = LineOfCursor(lines);
        var first = Math.Max(0, Math.Min(caretLine - inner.Height + 1, lines.Count - inner.Height));
        first = Math.Max(0, first);

        if (Value.Length == 0)
        {
            canvas.DrawText(inner.X, inner.Y, Text.Truncate(placeholder, inner.Width), Theme.Muted,
                maxWidth: inner.Width, background: fill);
        }

        for (var index = first; index < lines.Count && index - first < inner.Height; index++)
        {
            var line = lines[index];
            canvas.DrawText(inner.X, inner.Y + index - first, Value.Substring(line.Start, line.Length),
                Theme.Foreground, maxWidth: inner.Width, background: fill);
        }

        if (focused && caretLine >= first && caretLine - first < inner.Height)
        {
            var line = lines[caretLine];
            var column = Text.DisplayWidth(Value[line.Start..Cursor]);
            DrawCaret(canvas, inner.X + Math.Min(column, inner.Width - 1), inner.Y + caretLine - first,
                Under(placeholder), accent);
        }

        return Math.Min(inner.Height, Math.Max(1, lines.Count - first));
    }

    private static void DrawCaret(Canvas canvas, int x, int y, string under, Rgb accent) =>
        canvas.DrawText(x, y, under, Theme.OnAccent, bold: true, maxWidth: 1, background: accent);

    /// <summary>
    /// The glyph the caret sits on top of. An empty field is showing its placeholder, so the caret
    /// has to keep that first character visible rather than blanking it — otherwise the hint the
    /// field exists to give arrives with its first letter missing.
    /// </summary>
    private string Under(string placeholder)
    {
        if (Value.Length == 0)
        {
            return Text.Elements(placeholder).FirstOrDefault() ?? " ";
        }

        return Cursor >= 0 && Cursor < Value.Length && Value[Cursor] != '\n'
            ? Value[Cursor].ToString()
            : " ";
    }

    /// <summary>Returns the substring visible in the display columns [scroll, scroll + width).</summary>
    private static string Slice(string value, int scroll, int width)
    {
        var output = new System.Text.StringBuilder();
        var column = 0;
        foreach (var element in Text.Elements(value))
        {
            var elementWidth = Text.DisplayWidth(element);
            if (column >= scroll && column + elementWidth <= scroll + width)
            {
                output.Append(element);
            }

            column += elementWidth;
            if (column >= scroll + width)
            {
                break;
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// Greedy word wrap that reports spans into <see cref="Value"/> rather than detached strings,
    /// which is what lets the caret's character index be mapped back to a row and a column.
    /// </summary>
    internal static List<Line> Layout(string value, int width)
    {
        var lines = new List<Line>();
        if (width <= 0)
        {
            lines.Add(new Line(0, 0));
            return lines;
        }

        var position = 0;
        while (true)
        {
            var hardEnd = value.IndexOf('\n', position);
            if (hardEnd < 0)
            {
                hardEnd = value.Length;
            }

            var lineStart = position;
            while (true)
            {
                var used = 0;
                var breakAfterSpace = -1;
                var cursor = lineStart;
                while (cursor < hardEnd)
                {
                    var elementWidth = Text.DisplayWidth(value[cursor].ToString());
                    if (used + elementWidth > width)
                    {
                        break;
                    }

                    used += elementWidth;
                    if (value[cursor] == ' ')
                    {
                        breakAfterSpace = cursor + 1;
                    }

                    cursor++;
                }

                if (cursor >= hardEnd)
                {
                    lines.Add(new Line(lineStart, hardEnd - lineStart));
                    break;
                }

                var end = breakAfterSpace > lineStart ? breakAfterSpace : cursor;
                lines.Add(new Line(lineStart, end - lineStart));
                lineStart = end;
            }

            if (hardEnd >= value.Length)
            {
                break;
            }

            position = hardEnd + 1;
        }

        return lines;
    }

    private int LineOfCursor(List<Line> lines)
    {
        var found = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Start <= Cursor && Cursor <= lines[index].End)
            {
                found = index;
            }
        }

        return found;
    }

    private void Insert(string text)
    {
        Value = Value[..Cursor] + text + Value[Cursor..];
        Cursor += text.Length;
    }

    private void DeleteRange(int start, int end)
    {
        start = Math.Clamp(start, 0, Value.Length);
        end = Math.Clamp(end, 0, Value.Length);
        if (start >= end)
        {
            return;
        }

        Value = Value[..start] + Value[end..];
        Cursor = start;
    }

    /// <summary>Moves by one character, stepping over a surrogate pair rather than into it.</summary>
    private int Step(int direction)
    {
        var next = Math.Clamp(Cursor + direction, 0, Value.Length);
        if (direction < 0 && next > 0 && char.IsLowSurrogate(Value[next]))
        {
            next--;
        }
        else if (direction > 0 && next < Value.Length && char.IsLowSurrogate(Value[next]))
        {
            next++;
        }

        return next;
    }

    private int PreviousWord()
    {
        var index = Cursor;
        while (index > 0 && char.IsWhiteSpace(Value[index - 1]))
        {
            index--;
        }

        while (index > 0 && !char.IsWhiteSpace(Value[index - 1]))
        {
            index--;
        }

        return index;
    }

    private int NextWord()
    {
        var index = Cursor;
        while (index < Value.Length && !char.IsWhiteSpace(Value[index]))
        {
            index++;
        }

        while (index < Value.Length && char.IsWhiteSpace(Value[index]))
        {
            index++;
        }

        return index;
    }
}
