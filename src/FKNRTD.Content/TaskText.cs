using FKNRTD.Domain;

namespace FKNRTD.Help;

/// <summary>
/// How a task's own fields are written when they are missing.
/// </summary>
/// <remarks>
/// Creating a task refuses an empty title and a brief too short to act on, so neither can be blank
/// in a task this product made. A task record is a JSON file, though, and a hand edit or a
/// half-finished write can leave one blank anyway - at which point the pipeline drew a row with an
/// identifier and nothing beside it, which reads as a bug in the renderer rather than as a fact
/// about the task.
/// </remarks>
public static class TaskText
{
    /// <summary>A task's title, or a note saying it has none and how it got that way.</summary>
    public static string Title(WorkflowTask task) =>
        string.IsNullOrWhiteSpace(task.Title) ? "(no title - this record was edited by hand)" : task.Title;

    /// <summary>A task's brief, or a note saying what its absence means for the agents.</summary>
    public static string Brief(WorkflowTask task) =>
        string.IsNullOrWhiteSpace(task.Brief)
            ? "(no brief - this record was edited by hand. Every agent on this task is sent the " +
              "brief, so running it would send them nothing to act on.)"
            : task.Brief;
}
