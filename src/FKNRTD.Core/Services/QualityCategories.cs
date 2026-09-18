using FKNRTD.Domain;

namespace FKNRTD.Services;

/// <summary>The kind of checking a verification command does, inferred from what it is called.</summary>
public enum QualityCategory
{
    /// <summary>Compiles the project.</summary>
    Build,

    /// <summary>Checks style or formatting.</summary>
    Lint,

    /// <summary>Checks types.</summary>
    Types,

    /// <summary>Checks for known vulnerabilities.</summary>
    Security,

    /// <summary>Everything else, which is by far the commonest case.</summary>
    Tests
}

/// <summary>
/// Which kind of checking each verification command does. A command is classified by its own text
/// and nothing else, because the workspace configures shell commands and never says what they are
/// for.
/// </summary>
/// <remarks>
/// This lives here, apart from the orchestrator that records results into a
/// <see cref="QualitySnapshot"/>, so that a screen can ask what a task's commands would cover
/// without waiting for them to run. The dashboard used to draw "BUILD ○  TEST ○  LINT ○" for every
/// task, including a workspace with no verification commands at all - three named checks that
/// would never run, on the same screen where doctor reports that nothing checks this work.
/// </remarks>
public static class QualityCategories
{
    /// <summary>What kind of checking this command does.</summary>
    public static QualityCategory Classify(string command)
    {
        if (Mentions(command, "build", "compile"))
        {
            return QualityCategory.Build;
        }

        if (Mentions(command, "lint", "format"))
        {
            return QualityCategory.Lint;
        }

        if (Mentions(command, "type"))
        {
            return QualityCategory.Types;
        }

        if (Mentions(command, "security", "audit"))
        {
            return QualityCategory.Security;
        }

        // Anything unrecognised is treated as a test, which is what an unrecognised command most
        // often is, and which keeps every configured command counted rather than silently ignored.
        return QualityCategory.Tests;
    }

    /// <summary>
    /// The kinds of checking a set of commands covers, in the order they are worth reading. Empty
    /// when there are no commands, which is a workspace where nothing checks the work at all.
    /// </summary>
    public static IReadOnlyList<QualityCategory> Covered(IEnumerable<string>? commands)
    {
        var found = new HashSet<QualityCategory>();
        foreach (var command in commands ?? [])
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                found.Add(Classify(command));
            }
        }

        return Order.Where(found.Contains).ToArray();
    }

    /// <summary>The short label a screen shows for a category.</summary>
    public static string Label(QualityCategory category) => category switch
    {
        QualityCategory.Build => "BUILD",
        QualityCategory.Lint => "LINT",
        QualityCategory.Types => "TYPES",
        QualityCategory.Security => "SEC",
        _ => "TEST"
    };

    /// <summary>The state a snapshot holds for a category.</summary>
    public static StageState State(QualitySnapshot quality, QualityCategory category) => category switch
    {
        QualityCategory.Build => quality.Build,
        QualityCategory.Lint => quality.Lint,
        QualityCategory.Types => quality.Types,
        QualityCategory.Security => quality.Security,
        _ => quality.Tests
    };

    private static readonly QualityCategory[] Order =
    [
        QualityCategory.Build,
        QualityCategory.Tests,
        QualityCategory.Types,
        QualityCategory.Lint,
        QualityCategory.Security
    ];

    private static bool Mentions(string command, params string[] words) =>
        words.Any(word => command.Contains(word, StringComparison.OrdinalIgnoreCase));
}
