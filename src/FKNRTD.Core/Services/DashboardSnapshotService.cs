using System.Diagnostics;
using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class DashboardSnapshotService
{
    private readonly StateStore _store;
    private readonly GitService _git;
    private readonly ClaimService _claims;
    private readonly MessageService _messages;
    private TimeSpan _previousCpu = Process.GetCurrentProcess().TotalProcessorTime;
    private DateTimeOffset _previousSample = DateTimeOffset.UtcNow;

    public DashboardSnapshotService(
        StateStore store,
        GitService git,
        ClaimService claims,
        MessageService messages)
    {
        _store = store;
        _git = git;
        _claims = claims;
        _messages = messages;
    }

    public async Task<DashboardSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var configTask = _store.LoadConfigAsync(cancellationToken);
        var gitTask = _git.GetSnapshotAsync(_store.Paths.Root, cancellationToken);
        var tasksTask = _store.LoadTasksAsync(cancellationToken);
        var agentsTask = _store.LoadAgentRuntimesAsync(cancellationToken);
        var usageTask = _store.LoadUsageAsync(cancellationToken);
        var claimsTask = _store.LoadClaimsAsync(cancellationToken);
        var messagesTask = _messages.GetCurrentAsync(50, cancellationToken);
        var eventsTask = _store.LoadEventsAsync(100, cancellationToken);

        await Task.WhenAll(
                configTask,
                gitTask,
                tasksTask,
                agentsTask,
                usageTask,
                claimsTask,
                messagesTask,
                eventsTask)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var agentStates = MarkStaleAgents(await agentsTask.ConfigureAwait(false),
            (await configTask.ConfigureAwait(false)).AgentStaleAfterSeconds, now);
        var fileClaims = await claimsTask.ConfigureAwait(false);
        return new DashboardSnapshot
        {
            Config = await configTask.ConfigureAwait(false),
            Git = await gitTask.ConfigureAwait(false),
            Tasks = (await tasksTask.ConfigureAwait(false))
                .OrderByDescending(task => task.UpdatedAt)
                .ToArray(),
            Agents = agentStates,
            Usage = await usageTask.ConfigureAwait(false),
            Claims = fileClaims,
            Conflicts = _claims.Detect(fileClaims, agentStates, now),
            Messages = await messagesTask.ConfigureAwait(false),
            Events = (await eventsTask.ConfigureAwait(false)).OrderByDescending(item => item.Timestamp).ToArray(),
            Resources = CaptureResources(now),
            CapturedAt = now
        };
    }

    private static IReadOnlyList<AgentRuntimeState> MarkStaleAgents(
        IReadOnlyList<AgentRuntimeState> states,
        int staleAfterSeconds,
        DateTimeOffset now)
    {
        foreach (var state in states)
        {
            if (now - state.UpdatedAt > TimeSpan.FromSeconds(staleAfterSeconds) &&
                state.State is AgentActivityState.Running or AgentActivityState.Planning or
                    AgentActivityState.Reviewing)
            {
                state.State = AgentActivityState.Unknown;
                // Not "stale runtime state". That names an internal condition; this names what
                // happened and what it does not prove, which is what somebody reading the radar
                // needs. A long compile and a dead process look identical from here.
                state.Intent = $"Nothing reported for over {staleAfterSeconds}s — press L for its last output";
            }
        }

        return states.OrderBy(state => state.AgentId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private ResourceSnapshot CaptureResources(DateTimeOffset now)
    {
        using var process = Process.GetCurrentProcess();
        var elapsed = now - _previousSample;
        var cpuDelta = process.TotalProcessorTime - _previousCpu;
        var cpu = elapsed.TotalMilliseconds <= 0
            ? 0
            : cpuDelta.TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100;
        _previousCpu = process.TotalProcessorTime;
        _previousSample = now;
        return new ResourceSnapshot
        {
            ProcessCpuPercent = Math.Clamp(cpu, 0, 100),
            WorkingSetBytes = process.WorkingSet64,
            ProcessorCount = Environment.ProcessorCount,
            CapturedAt = now
        };
    }
}
