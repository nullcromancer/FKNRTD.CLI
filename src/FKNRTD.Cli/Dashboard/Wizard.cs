using FKNRTD.Help;

namespace FKNRTD.Dashboard;

internal enum WizardInput
{
    /// <summary>One line of free text.</summary>
    Text,

    /// <summary>Several lines of free text, wrapped.</summary>
    LongText,

    /// <summary>Pick one of a list, each option carrying its own explanation.</summary>
    Choice,

    /// <summary>Several lines of free text, each line meaning one shell command.</summary>
    Commands,

    /// <summary>One line of text constrained to a whole number.</summary>
    Number
}

/// <summary>One selectable answer, with enough context that the choice can be made on the spot.</summary>
internal sealed record WizardOption(
    string Value,
    string Label,
    string Description,
    bool Recommended = false,
    string? Warning = null);

/// <summary>
/// One question in a wizard. A step is required to name a <see cref="GlossaryTerm"/>, which is how
/// every question ends up carrying a real explanation rather than a bare label: the wizard renders
/// the term's detail and example underneath the input.
/// </summary>
internal sealed class WizardStep
{
    public required string Key { get; init; }

    /// <summary>The question in plain language, as a person would ask it out loud.</summary>
    public required string Question { get; init; }

    /// <summary>Key into <see cref="Glossary"/>. The step's explanation comes from there.</summary>
    public required string GlossaryTerm { get; init; }

    public WizardInput Input { get; init; } = WizardInput.Text;

    public string Placeholder { get; init; } = string.Empty;

    /// <summary>The value offered when the step is first reached, given the answers so far.</summary>
    public Func<IReadOnlyDictionary<string, string>, string> Default { get; init; } = _ => string.Empty;

    public Func<IReadOnlyDictionary<string, string>, IReadOnlyList<WizardOption>> Options { get; init; } =
        _ => [];

    /// <summary>Returns an error to show, or null when the answer is acceptable.</summary>
    public Func<string, IReadOnlyDictionary<string, string>, string?> Validate { get; init; } =
        (_, _) => null;

    /// <summary>
    /// An explanation specific to this question, used instead of the glossary term's. A step that
    /// edits one named configuration field has a better answer to "what is this?" than the general
    /// term it belongs to; F1 still reaches the term itself.
    /// </summary>
    public string Explanation { get; init; } = string.Empty;

    /// <summary>A worked example specific to this question, used instead of the glossary term's.</summary>
    public string Example { get; init; } = string.Empty;

    /// <summary>The caption over <see cref="Example"/> when it is not an example at all.</summary>
    public string ExampleCaption { get; init; } = "EXAMPLE";

    /// <summary>Lets a step be skipped entirely — a Git-only question in a standalone workspace.</summary>
    public Func<IReadOnlyDictionary<string, string>, bool> Applies { get; init; } = _ => true;
}

/// <summary>
/// A guided, explained, multi-step form. It replaces the run of bare <c>Console.ReadLine</c> prompts
/// that used to ask an operator for a "brief", a "lead" and an "auditor" with no indication of what
/// any of those words meant.
/// </summary>
internal sealed class Wizard : IOverlay
{
    private readonly string _title;
    private readonly Rgb _accent;
    private readonly List<WizardStep> _steps;
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private TextField _field = new();
    private int _index;
    private int _choice;
    private string? _error;
    private bool _expanded;

    public Wizard(string title, Rgb accent, IEnumerable<WizardStep> steps, string finishVerb = "create")
    {
        _title = title;
        _accent = accent;
        _steps = steps.ToList();
        FinishVerb = finishVerb;
        _index = FirstApplicable(0, 1);
        LoadStep();
    }

    public string Mode => _title;

    /// <summary>
    /// What Enter does on the last step, for the footer. A form that changes one existing setting
    /// says "save"; telling the operator it will "create" something would be a small lie about what
    /// the key is about to do.
    /// </summary>
    private string FinishVerb { get; }

    public IReadOnlyDictionary<string, string> Values => _values;

    public string Value(string key) => _values.TryGetValue(key, out var value) ? value : string.Empty;

    /// <summary>Answers split into lines, for the steps that mean one item per line.</summary>
    public IReadOnlyList<string> Lines(string key) => Value(key)
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToArray();

    public OverlayResult HandleKey(ConsoleKeyInfo key)
    {
        // Escape is checked first. A form whose steps all turn out not to apply would otherwise
        // report a successful completion in response to the operator backing out of it.
        if (key.Key == ConsoleKey.Escape)
        {
            return OverlayResult.Cancel;
        }

        var step = Current;
        if (step is null)
        {
            return OverlayResult.Submit;
        }

        if (key.Key == ConsoleKey.F1)
        {
            _expanded = !_expanded;
            return OverlayResult.Continue;
        }

        if (_expanded)
        {
            // While the full explanation is showing, every key returns to the question rather than
            // silently editing a field the operator cannot currently see.
            _expanded = false;
            return OverlayResult.Continue;
        }

        var back = key.Key == ConsoleKey.PageUp ||
                   (key.Key == ConsoleKey.Tab && (key.Modifiers & ConsoleModifiers.Shift) != 0);
        if (back)
        {
            Store(step);
            var previous = FirstApplicable(_index - 1, -1);
            if (previous >= 0 && previous < _index)
            {
                _index = previous;
                _error = null;
                LoadStep();
            }

            return OverlayResult.Continue;
        }

        if (step.Input == WizardInput.Choice)
        {
            var options = step.Options(_values);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    _choice = Math.Max(0, _choice - 1);
                    return OverlayResult.Continue;
                case ConsoleKey.DownArrow:
                    _choice = Math.Min(Math.Max(0, options.Count - 1), _choice + 1);
                    return OverlayResult.Continue;
                case ConsoleKey.Enter:
                    return Advance(step, options.ElementAtOrDefault(_choice)?.Value ?? string.Empty);
            }

            // Typing a leading character jumps to the matching option, the way a select box does.
            if (!char.IsControl(key.KeyChar))
            {
                var match = options
                    .Select((option, index) => (option, index))
                    .FirstOrDefault(item =>
                        item.option.Value.StartsWith(key.KeyChar.ToString(), StringComparison.OrdinalIgnoreCase));
                if (match.option is not null)
                {
                    _choice = match.index;
                }
            }

            return OverlayResult.Continue;
        }

        if (_field.HandleKey(key))
        {
            _error = null;
            return OverlayResult.Continue;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            var answer = _field.Value.Trim();
            if (answer.Length == 0)
            {
                answer = step.Default(_values);
            }

            return Advance(step, answer);
        }

        return OverlayResult.Continue;
    }

    private OverlayResult Advance(WizardStep step, string answer)
    {
        var problem = step.Validate(answer, _values);
        if (problem is not null)
        {
            _error = problem;
            return OverlayResult.Continue;
        }

        _values[step.Key] = answer;
        _error = null;
        var next = FirstApplicable(_index + 1, 1);
        if (next < 0)
        {
            FillUnanswered();
            return OverlayResult.Submit;
        }

        _index = next;
        LoadStep();
        return OverlayResult.Continue;
    }

    /// <summary>
    /// Gives every step that was never reached its default, in order, before the answers are read.
    /// A step skipped by <see cref="WizardStep.Applies"/> is skipped because its default is already
    /// right — not because it has no answer — and the caller must not receive an empty string for it.
    /// Order matters: a later default can depend on an earlier one.
    /// </summary>
    private void FillUnanswered()
    {
        foreach (var step in _steps)
        {
            if (_values.ContainsKey(step.Key))
            {
                continue;
            }

            var fallback = step.Default(_values);
            if (fallback.Length == 0 && step.Input == WizardInput.Choice)
            {
                fallback = step.Options(_values).FirstOrDefault()?.Value ?? string.Empty;
            }

            _values[step.Key] = fallback;
        }
    }

    private WizardStep? Current => _index >= 0 && _index < _steps.Count ? _steps[_index] : null;

    private int FirstApplicable(int from, int direction)
    {
        for (var index = from; index >= 0 && index < _steps.Count; index += direction)
        {
            if (_steps[index].Applies(_values))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Keeps the current answer before navigating away from a step, so going back and forward again
    /// returns what was there rather than resetting to the default.
    /// </summary>
    private void Store(WizardStep step)
    {
        if (step.Input != WizardInput.Choice)
        {
            _values[step.Key] = _field.Value.Trim();
            return;
        }

        var highlighted = step.Options(_values).ElementAtOrDefault(_choice);
        if (highlighted is not null)
        {
            _values[step.Key] = highlighted.Value;
        }
    }

    private void LoadStep()
    {
        var step = Current;
        if (step is null)
        {
            return;
        }

        var existing = _values.TryGetValue(step.Key, out var value) && value.Length > 0
            ? value
            : step.Default(_values);
        if (step.Input == WizardInput.Choice)
        {
            var options = step.Options(_values);
            var index = options
                .Select((option, position) => (option, position))
                .FirstOrDefault(item => item.option.Value.Equals(existing, StringComparison.OrdinalIgnoreCase));
            _choice = index.option is null ? 0 : index.position;
            return;
        }

        _field = new TextField(existing, step.Input is WizardInput.LongText or WizardInput.Commands);
    }

    public void Draw(Canvas canvas, Rect area)
    {
        canvas.Dim(area, Overlays.ScrimForeground, Overlays.ScrimBackground);
        var step = Current;
        if (step is null)
        {
            return;
        }

        var entry = Glossary.Find(step.GlossaryTerm);
        var contentWidth = Math.Max(10, Math.Min(98, area.Width - 4) - 6);
        var panel = Overlays.Centre(area, 98, _expanded ? 28 : Measure(step, entry, contentWidth));
        canvas.DrawPanel(panel, _title, _accent, Theme.Surface);
        var x = panel.X + 3;
        var width = Math.Max(10, panel.Width - 6);
        var lastRow = panel.Bottom - 4;

        if (_expanded)
        {
            DrawExpanded(canvas, panel, x, width, lastRow, entry);
            Overlays.Footer(canvas, panel, _accent, ("F1", "back to the question"), ("Esc", "cancel"));
            return;
        }

        var row = DrawProgress(canvas, panel, x, width, entry?.Title ?? step.Key);
        row = canvas.DrawWrapped(x, row, width, 2, step.Question, _accent, bold: true, background: Theme.Surface);
        row++;

        row = step.Input switch
        {
            WizardInput.Choice => DrawChoices(canvas, step, x, row, width, lastRow),
            _ => DrawField(canvas, step, x, row, width, lastRow)
        };

        row++;
        if (_error is not null)
        {
            row = canvas.DrawWrapped(x, row, width, 2, "× " + _error, Theme.Red, bold: true,
                background: Theme.Surface);
            row++;
        }

        var detail = Detail(step, entry);
        if (detail.Length > 0 && row < lastRow)
        {
            var (example, caption) = Illustration(step, entry);
            // lastRow is the final usable row, so it counts towards the budget.
            var remaining = lastRow - row + 1;
            // The detail answers the question and the second block only illustrates it, so the
            // detail is served first. Serving the illustration first starved a long explanation
            // down to a single clipped line.
            var detailRows = Math.Min(Text.Wrap(detail, width).Count + 1, remaining);
            // A block shown as half a sentence teaches less than no block at all, so on a terminal
            // too short to hold the whole thing it is dropped rather than clipped.
            var exampleNeeds = example.Length == 0 ? 0 : Text.Wrap(example, width).Count + 1;
            var exampleRows = exampleNeeds > 0 && detailRows + 1 + exampleNeeds <= remaining
                ? exampleNeeds
                : 0;
            var after = Overlays.Explain(canvas, x, row, width, detailRows, "WHAT THIS IS", detail, _accent);
            if (exampleRows > 1 && after + 1 < lastRow)
            {
                Overlays.Explain(canvas, x, after + 1, width, exampleRows, caption, example, _accent);
            }

            // There is deliberately no "there is more, press F1" marker here. On a narrow terminal it
            // had nowhere to go but on top of the text it was describing, and the footer already
            // carries F1 on every step.
        }

        Overlays.Footer(canvas, panel, _accent, FooterKeys(step));
    }

    /// <summary>
    /// A worked example earns its space for a field the operator has to write, and wastes it for one
    /// where they are picking from a list that already shows every answer.
    /// </summary>
    private static bool ShowsExample(WizardStep step, GlossaryEntry entry) =>
        step.Input != WizardInput.Choice && entry.Example.Length > 0;

    /// <summary>
    /// The panel's height, from what this step actually has to say. Sizing to content rather than to
    /// a fixed box keeps a two-line question from being framed by fifteen lines of nothing.
    /// </summary>
    /// <summary>
    /// The body of the "what this is" block: the step's own explanation when it has one, and the
    /// glossary term's otherwise. Measuring and drawing both read this, because the last time they
    /// each decided for themselves the panel clipped its own text.
    /// </summary>
    private static string Detail(WizardStep step, GlossaryEntry? entry) =>
        step.Explanation.Length > 0 ? step.Explanation : entry?.Detail ?? string.Empty;

    /// <summary>The body of the second block, and the caption that fits whatever it turned out to be.</summary>
    private static (string Body, string Caption) Illustration(WizardStep step, GlossaryEntry? entry) =>
        step.Example.Length > 0
            ? (step.Example, step.ExampleCaption)
            : entry is not null && ShowsExample(step, entry) ? (entry.Example, "EXAMPLE") : (string.Empty, "");

    private int Measure(WizardStep step, GlossaryEntry? entry, int width)
    {
        var rows = 2;                                              // progress row, then a gap
        rows += Math.Min(2, Text.Wrap(step.Question, width).Count); // the question
        rows += 1;                                                  // gap before the input
        rows += step.Input switch
        {
            WizardInput.LongText => 6,
            WizardInput.Commands => 4,
            WizardInput.Choice => Math.Max(1, Math.Min(8, step.Options(_values).Count)),
            _ => 1
        };
        rows += 1;                                                  // gap after the input
        if (_error is not null)
        {
            rows += Math.Min(2, Text.Wrap(_error, width).Count) + 1;
        }

        var detail = Detail(step, entry);
        if (detail.Length > 0)
        {
            rows += Text.Wrap(detail, width).Count + 1;
            if (Illustration(step, entry).Body is { Length: > 0 } illustration)
            {
                rows += Text.Wrap(illustration, width).Count + 2;
            }
        }

        rows += 4;                                                  // rule, footer, and both borders
        return rows;
    }

    private (string Key, string Meaning)[] FooterKeys(WizardStep step)
    {
        var confirm = LastApplicable() == _index ? FinishVerb : "next";
        var keys = new List<(string, string)>();
        if (step.Input == WizardInput.Choice)
        {
            keys.Add(("↑↓", "choose"));
        }

        keys.Add(("Enter", confirm));
        if (step.Input is WizardInput.LongText or WizardInput.Commands)
        {
            keys.Add(("Alt+Enter", "new line"));
        }

        if (FirstApplicable(_index - 1, -1) >= 0)
        {
            keys.Add(("Shift+Tab", "back"));
        }

        keys.Add(("F1", "explain"));
        keys.Add(("Esc", "cancel"));
        return keys.ToArray();
    }

    private int LastApplicable()
    {
        var last = _index;
        for (var index = _index + 1; index < _steps.Count; index++)
        {
            if (_steps[index].Applies(_values))
            {
                last = index;
            }
        }

        return last;
    }

    /// <summary>Draws the step dots and the "Step n of m" caption.</summary>
    private int DrawProgress(Canvas canvas, Rect panel, int x, int width, string stepTitle)
    {
        var applicable = Enumerable.Range(0, _steps.Count).Where(index => _steps[index].Applies(_values)).ToArray();
        var position = Array.IndexOf(applicable, _index);
        var dots = string.Concat(applicable.Select((_, index) =>
            (index == 0 ? string.Empty : "──") + (index <= position ? "●" : "○")));
        canvas.DrawText(x, panel.Y + 1, dots, _accent, bold: true, maxWidth: width, background: Theme.Surface);

        var caption = $"Step {position + 1} of {applicable.Length} · {stepTitle}";
        var captionWidth = Math.Min(Text.DisplayWidth(caption), Math.Max(0, width - Text.DisplayWidth(dots) - 2));
        if (captionWidth > 0)
        {
            canvas.DrawText(panel.Right - 3 - captionWidth, panel.Y + 1, caption, Theme.Muted,
                maxWidth: captionWidth, background: Theme.Surface);
        }

        return panel.Y + 3;
    }

    private int DrawField(Canvas canvas, WizardStep step, int x, int row, int width, int lastRow)
    {
        var height = step.Input switch
        {
            WizardInput.LongText => Math.Clamp(lastRow - row - 8, 3, 6),
            WizardInput.Commands => Math.Clamp(lastRow - row - 8, 2, 4),
            _ => 1
        };
        var rect = new Rect(x, row, width, Math.Max(1, height));
        _field.Draw(canvas, rect, focused: true, step.Placeholder, _accent);
        return row + rect.Height;
    }

    private int DrawChoices(Canvas canvas, WizardStep step, int x, int row, int width, int lastRow)
    {
        var options = step.Options(_values);
        if (options.Count == 0)
        {
            canvas.DrawText(x, row, "Nothing to choose from. Press Esc, then A to set this up.", Theme.Amber,
                maxWidth: width, background: Theme.Surface);
            return row + 1;
        }

        _choice = Math.Clamp(_choice, 0, options.Count - 1);
        // Measure already gave the panel a row per option, so the only ceiling here is the panel
        // itself. The old constant guess predates content-sized panels and could hide an option
        // entirely — which on a two-option step means hiding that there was a choice at all.
        var visible = Math.Max(1, Math.Min(options.Count, lastRow - row + 1));
        var first = Math.Clamp(_choice - visible / 2, 0, Math.Max(0, options.Count - visible));
        // The cap only binds on a label longer than it, so raising it changes nothing for the forms
        // whose labels are short and stops truncating the ones that are not: "Yes, edit this folder"
        // was arriving as "Yes, edit this ...".
        var labelWidth = Math.Min(22, options.Max(option => Text.DisplayWidth(option.Label)) + 1);

        for (var index = first; index < first + visible && index < options.Count; index++)
        {
            var option = options[index];
            var selected = index == _choice;
            var y = row + index - first;
            var fill = selected ? Theme.SurfaceRaised : Theme.Surface;
            canvas.Fill(new Rect(x, y, width, 1), fill);
            canvas.DrawText(x, y, selected ? "▸ " : "  ", _accent, bold: true, maxWidth: 2, background: fill);
            canvas.DrawText(x + 2, y, Text.Truncate(option.Label, labelWidth), selected ? Theme.Foreground : Theme.Muted,
                bold: selected, maxWidth: labelWidth, background: fill);

            var detail = option.Warning is not null
                ? option.Description + " · " + option.Warning
                : option.Recommended
                    ? option.Description + " · recommended"
                    : option.Description;
            // Two columns of gutter, not one. A label that exactly fills its column - "Stop it and
            // quit" is sixteen characters against a sixteen-wide column - otherwise runs straight
            // into its own description with a single space between them.
            var detailX = x + 2 + labelWidth + 2;
            var detailWidth = Math.Max(0, width - (detailX - x));
            canvas.DrawText(detailX, y, Text.Truncate(detail, detailWidth),
                option.Warning is not null ? Theme.Amber : selected ? Theme.Muted : Theme.Muted.Blend(fill, 0.25),
                maxWidth: detailWidth, background: fill);
        }

        return row + visible;
    }

    private void DrawExpanded(Canvas canvas, Rect panel, int x, int width, int lastRow, GlossaryEntry? entry)
    {
        var row = panel.Y + 1;
        if (entry is null)
        {
            canvas.DrawText(x, row, "No explanation is recorded for this field.", Theme.Muted, maxWidth: width,
                background: Theme.Surface);
            return;
        }

        canvas.DrawText(x, row, entry.Title.ToUpperInvariant(), _accent, bold: true, maxWidth: width,
            background: Theme.Surface);
        row += 2;
        row = canvas.DrawWrapped(x, row, width, 2, entry.Summary, Theme.Foreground, bold: true,
            background: Theme.Surface);
        row++;
        row = canvas.DrawWrapped(x, row, width, lastRow - row - 4, entry.Detail, Theme.Foreground,
            background: Theme.Surface);
        if (entry.Example.Length > 0 && row + 1 < lastRow)
        {
            Overlays.Explain(canvas, x, row + 1, width, lastRow - row - 1, "EXAMPLE", entry.Example, _accent);
        }
    }
}
