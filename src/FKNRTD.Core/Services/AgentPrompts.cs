using FKNRTD.Domain;

namespace FKNRTD.Services;

/// <summary>
/// The instructions each agent is sent, as pure functions of the task.
/// </summary>
/// <remarks>
/// These were written inline in the orchestrator, where nothing outside a live run could see them.
/// That made the one thing an operator most needs before authorising an agent — what the agent is
/// actually going to be told — the one thing the product would not show. Composed here instead, so
/// the same text that reaches the agent can be previewed beforehand and asserted about in a test.
/// </remarks>
public static class AgentPrompts
{
    /// <summary>The lead's instruction: read the code and the brief, propose a plan, change nothing.</summary>
    public static string Plan(WorkflowTask task) =>
        $"""
        You are the lead developer for FKNRTD.CLI task {task.Id}.
        Work in read-only planning mode. Inspect the repository and create a concise implementation plan for this brief:

        {task.Brief}

        Include exact files likely to change, risks, acceptance criteria, and the verification approach.
        Do not edit files and do not claim implementation is complete.
        """;

    /// <summary>
    /// The implementer's instruction: the brief, the lead's plan, and — on a repair round — the
    /// failures it is being asked to correct. This is the only prompt sent to an agent that can write.
    /// </summary>
    public static string Implement(WorkflowTask task, WorkspaceMode mode, string plan, string repairContext) =>
        $"""
        You are the implementing developer for FKNRTD.CLI task {task.Id}.
        {Isolation(task, mode)}

        Brief:
        {task.Brief}

        Lead plan:
        {plan}
        {repairContext}

        Implement the requested change completely in this workspace. Keep unrelated files untouched.
        You may run focused checks. Do not merge anything and do not edit outside this workspace.
        Report the changed files and the checks you ran when finished.
        """;

    /// <summary>The auditor's instruction: judge the finished work against the brief, read-only, and end with a verdict.</summary>
    public static string Audit(WorkflowTask task, WorkspaceMode mode, string verificationSummary) =>
        $"""
        You are the independent auditor for FKNRTD.CLI task {task.Id}.
        {Against(task, mode)}

        {task.Brief}

        Verification results:
        {verificationSummary}

        Inspect the actual changes and evidence. Do not edit any file.
        Finish with exactly one verdict marker on its own line:
        FKNRTD_VERDICT: PASS
        or
        FKNRTD_VERDICT: FAIL
        A PASS means the implementation satisfies the brief and has no blocking correctness, safety, or maintainability issue.
        """;

    /// <summary>The paragraph that tells the implementer where it is, and therefore what it may touch.</summary>
    public static string Isolation(WorkflowTask task, WorkspaceMode mode) =>
        mode == WorkspaceMode.Standalone
            ? "You are working directly in the FKNRTD.CLI workspace" +
              (string.IsNullOrWhiteSpace(task.WorktreePath) ? string.Empty : $" at {task.WorktreePath}") +
              ". There is no Git isolation, so change only what the brief requires."
            // The branch is always set by the time this reaches an agent, because the worktree stage
            // runs first. It is empty when the prompt is previewed beforehand, and "on branch ."
            // reads as a bug rather than as a task that has not started.
            : string.IsNullOrWhiteSpace(task.BranchName)
                ? "You are already inside an isolated Git worktree on this task's own branch."
                : $"You are already inside an isolated Git worktree on branch {task.BranchName}.";

    private static string Against(WorkflowTask task, WorkspaceMode mode) =>
        mode == WorkspaceMode.Standalone
            ? "Review the implementation in this workspace against this brief:"
            : $"Review the implementation in this worktree against base ref {task.BaseRef} and this brief:";
}
