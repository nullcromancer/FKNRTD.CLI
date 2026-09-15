using FKNRTD.Dashboard;
using FKNRTD.Services;
using FKNRTD.Telemetry;

namespace FKNRTD.Commands;

internal sealed class FknrtdRuntime
{
    public FknrtdRuntime(FknrtdPaths paths)
    {
        Paths = paths;
        Store = new StateStore(paths);
        Processes = new ProcessRunner();
        Git = new GitService(Processes);
        Worktrees = new WorktreeService(Git, Store);
        Tasks = new TaskService(Store, Git);
        Messages = new MessageService(Store);
        Claims = new ClaimService(Store);
        Usage = new UsageService(Store);
        Agents = new AgentRunner(Store, Processes);
        Orchestrator = new Orchestrator(Store, Git, Worktrees, Agents, Processes);
        Snapshots = new DashboardSnapshotService(Store, Git, Claims, Messages);
        Doctor = new DoctorService(Store, Git, Processes);
        Dashboard = new DashboardApp(Snapshots, Orchestrator, Tasks, Messages, Usage, Store);
    }

    public FknrtdPaths Paths { get; }
    public StateStore Store { get; }
    public ProcessRunner Processes { get; }
    public GitService Git { get; }
    public WorktreeService Worktrees { get; }
    public TaskService Tasks { get; }
    public MessageService Messages { get; }
    public ClaimService Claims { get; }
    public UsageService Usage { get; }
    public AgentRunner Agents { get; }
    public Orchestrator Orchestrator { get; }
    public DashboardSnapshotService Snapshots { get; }
    public DoctorService Doctor { get; }
    public DashboardApp Dashboard { get; }
}
