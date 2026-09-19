namespace FKNRTD.Help;

/// <summary>
/// The one-line note shown beside each of the three roles.
/// </summary>
/// <remarks>
/// These sentences existed in four places: the dashboard's prompt preview, the shell's, the task
/// inspector's, and the shell's `task show`. All four said the lead and auditor <em>cannot</em>
/// write and the implementer is the <em>only</em> agent that may — which is what the shipped
/// profiles ask for and not what anything enforces. Correcting that meant finding four copies, and
/// the first pass found two. They live here now, for the same reason every other explanation in
/// this product reads from a table: a sentence in one place can be corrected, and a sentence in
/// four places will be corrected in one of them.
/// </remarks>
public static class RoleNotes
{
    /// <summary>What the lead is asked to do, and the limit of what that guarantees.</summary>
    public const string Lead =
        "Its profile asks it not to edit, and the prompt says so too. It proposes.";

    /// <summary>What the implementer is asked to do.</summary>
    public const string Implementer =
        "The one agent asked to write files. This prompt carries the lead's plan.";

    /// <summary>What the auditor is asked to do, and what it must produce.</summary>
    public const string Auditor =
        "Its profile asks it not to edit. It must end with a PASS or FAIL verdict.";

    /// <summary>The shorter forms, for a list of roles rather than a prompt preview.</summary>
    public const string LeadShort = "reads the code and writes the plan";

    public const string ImplementerShort = "the agent asked to change files";

    public const string AuditorShort = "judges the finished work and must return PASS";
}
