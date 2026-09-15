namespace FKNRTD.Domain;

public sealed record FknrtdConfig
{
    public int SchemaVersion { get; init; } = 1;
    public string ProjectName { get; init; } = string.Empty;
    public WorkspaceMode Mode { get; init; } = WorkspaceMode.Git;
    public string DefaultBaseRef { get; init; } = string.Empty;
    public int MaxParallelAgents { get; init; } = 4;
    public int DefaultMaxRepairRounds { get; init; } = 1;
    public int AgentStaleAfterSeconds { get; init; } = 120;
    public int ClaimStaleAfterSeconds { get; init; } = 300;
    public int AgentTimeoutSeconds { get; init; } = 3600;
    public int VerificationTimeoutSeconds { get; init; } = 600;
    public int DashboardRefreshMilliseconds { get; init; } = 1000;
    public bool RequireCleanTreeForLanding { get; init; } = true;
    public bool AutoCommitAgentChanges { get; init; } = true;
    public List<string> DefaultVerificationCommands { get; init; } = [];
    public List<AgentDefinition> Agents { get; init; } = [];
}

public sealed record AgentDefinition
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Kind { get; init; } = "generic";
    public string Executable { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public string Color { get; init; } = "cyan";
    public Dictionary<string, AgentCommandProfile> Profiles { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Environment { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed record AgentCommandProfile
{
    public List<string> Arguments { get; init; } = [];
    public PromptDelivery PromptDelivery { get; init; } = PromptDelivery.Argument;
    public string? SuccessMarker { get; init; }
    public string? FailureMarker { get; init; }
}

public static class BuiltInAgents
{
    public static IReadOnlyList<AgentDefinition> CreateDefaults() =>
    [
        new AgentDefinition
        {
            Id = "claude",
            DisplayName = "Claude",
            Kind = "claude",
            Executable = "claude",
            Color = "orange",
            Profiles = new Dictionary<string, AgentCommandProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["plan"] = new()
                {
                    Arguments = ["-p", "{prompt}", "--output-format", "stream-json", "--verbose", "--permission-mode", "plan", "--permission-prompts", "none"]
                },
                ["implement"] = new()
                {
                    Arguments = ["-p", "{prompt}", "--output-format", "stream-json", "--verbose", "--permission-mode", "acceptEdits", "--permission-prompts", "none"]
                },
                ["audit"] = new()
                {
                    Arguments = ["-p", "{prompt}", "--output-format", "stream-json", "--verbose", "--permission-mode", "plan", "--permission-prompts", "none"],
                    SuccessMarker = "FKNRTD_VERDICT: PASS",
                    FailureMarker = "FKNRTD_VERDICT: FAIL"
                },
                ["default"] = new() { Arguments = ["-p", "{prompt}"] }
            }
        },
        new AgentDefinition
        {
            Id = "codex",
            DisplayName = "Codex",
            Kind = "codex",
            Executable = "codex",
            Color = "violet",
            Profiles = new Dictionary<string, AgentCommandProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["plan"] = new()
                {
                    Arguments = ["exec", "--json", "--sandbox", "read-only", "{prompt}"]
                },
                ["implement"] = new()
                {
                    Arguments = ["exec", "--json", "--sandbox", "workspace-write", "{prompt}"]
                },
                ["audit"] = new()
                {
                    Arguments = ["exec", "--json", "--sandbox", "read-only", "{prompt}"],
                    SuccessMarker = "FKNRTD_VERDICT: PASS",
                    FailureMarker = "FKNRTD_VERDICT: FAIL"
                },
                ["default"] = new() { Arguments = ["exec", "{prompt}"] }
            }
        }
    ];
}
