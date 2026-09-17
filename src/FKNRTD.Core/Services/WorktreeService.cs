using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class WorktreeService
{
    private readonly GitService _git;
    private readonly StateStore _store;

    public WorktreeService(GitService git, StateStore store)
    {
        _git = git;
        _store = store;
    }

    public async Task<(string Path, string Branch)> CreateAsync(
        WorkflowTask task,
        WorkspaceMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode == WorkspaceMode.Standalone)
        {
            // A standalone workspace has no Git isolation to offer: agents work in the root itself.
            task.BaseRef = string.Empty;
            return (_store.Paths.Root, string.Empty);
        }

        if (!await _git.IsRepositoryAsync(_store.Paths.Root, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "This task needs an isolated worktree, which only a Git repository can provide. Either " +
                "make this folder a repository with 'git init', or re-run " +
                "'fknrtd init -force -standalone' to work without isolation — agents will then edit this " +
                "folder directly, with nothing to roll back to.");
        }

        task.BaseRef = await _git.ResolveBaseBranchAsync(
                _store.Paths.Root,
                task.BaseRef,
                cancellationToken)
            .ConfigureAwait(false);

        var branch = string.IsNullOrWhiteSpace(task.BranchName)
            ? $"fknrtd/{Slug(task.Id)}-{Slug(task.Title, 36)}"
            : task.BranchName;
        var path = string.IsNullOrWhiteSpace(task.WorktreePath)
            ? Path.Combine(_store.Paths.Worktrees, StateStore.SafeName(task.Id))
            : task.WorktreePath;

        if (Directory.Exists(path) &&
            (Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"))))
        {
            return (path, branch);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var branchExists = await _git.BranchExistsAsync(_store.Paths.Root, branch, cancellationToken)
            .ConfigureAwait(false);
        var arguments = branchExists
            ? new[] { "worktree", "add", path, branch }
            : new[] { "worktree", "add", "-b", branch, path, task.BaseRef };
        var result = await _git.GitAsync(_store.Paths.Root, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Git could not create the task's isolated worktree, so the task cannot start. Nothing in " +
                "your checkout was touched. A stale worktree of the same name is the usual cause; " +
                "'git worktree prune' clears those. Git said: " + result.StandardError.Trim());
        }

        return (path, branch);
    }

    public async Task<CommandResult> LandAsync(
        WorkflowTask task,
        bool requireCleanTree,
        WorkspaceMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode == WorkspaceMode.Standalone)
        {
            // Nothing was ever branched off, so the verified work is already in place.
            return new CommandResult { ExitCode = 0 };
        }

        var snapshot = await _git.GetSnapshotAsync(_store.Paths.Root, cancellationToken).ConfigureAwait(false);
        var baseBranch = await _git.ResolveBaseBranchAsync(
                _store.Paths.Root,
                task.BaseRef,
                cancellationToken)
            .ConfigureAwait(false);
        task.BaseRef = baseBranch;
        if (requireCleanTree && !snapshot.IsClean)
        {
            throw new InvalidOperationException(
                "Landing is blocked because your own checkout has uncommitted changes, and merging on " +
                "top of them would mix verified work with work nothing has checked. Commit or stash " +
                "them, then land again. The task is unaffected and stays ready.");
        }

        if (!string.Equals(snapshot.Branch, baseBranch, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Landing is blocked because your checkout is on '{snapshot.Branch}' and this task " +
                $"merges into '{baseBranch}'. Switch to '{baseBranch}' and land again. The task is " +
                "unaffected and stays ready.");
        }

        var result = await _git.GitAsync(
                _store.Paths.Root,
                ["merge", "--no-ff", "--no-edit", task.BranchName],
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            await _git.GitAsync(_store.Paths.Root, ["merge", "--abort"], cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task RemoveAsync(
        WorkflowTask task,
        bool force,
        WorkspaceMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode == WorkspaceMode.Standalone)
        {
            // There is no worktree and no task branch to reclaim.
            return;
        }

        if (!string.IsNullOrWhiteSpace(task.WorktreePath) && Directory.Exists(task.WorktreePath))
        {
            var arguments = force
                ? new[] { "worktree", "remove", "--force", task.WorktreePath }
                : new[] { "worktree", "remove", task.WorktreePath };
            var remove = await _git.GitAsync(_store.Paths.Root, arguments, cancellationToken).ConfigureAwait(false);
            if (!remove.Success)
            {
                throw new InvalidOperationException(
                    "Git could not remove the task's worktree directory. Nothing was deleted, and the " +
                    "task record and branch are untouched. A process holding a file open inside it is " +
                    "the usual cause. Git said: " + remove.StandardError.Trim());
            }
        }

        var prune = await _git.GitAsync(_store.Paths.Root, ["worktree", "prune"], cancellationToken)
            .ConfigureAwait(false);
        if (!prune.Success)
        {
            throw new InvalidOperationException(
                "The worktree directory was removed, but Git could not prune its own record of it. Run " +
                "'git worktree prune' in the repository to finish tidying up. Git said: " +
                prune.StandardError.Trim());
        }

        if (task.Status == WorkflowStatus.Landed &&
            !string.IsNullOrWhiteSpace(task.BranchName) &&
            await _git.BranchExistsAsync(_store.Paths.Root, task.BranchName, cancellationToken).ConfigureAwait(false))
        {
            var delete = await _git.GitAsync(
                    _store.Paths.Root,
                    ["branch", "-d", task.BranchName],
                    cancellationToken)
                .ConfigureAwait(false);
            if (!delete.Success)
            {
                throw new InvalidOperationException(
                    "The worktree was removed, but the landed task branch could not be deleted. It is " +
                    "harmless to leave; 'git branch -d' removes it later. Git said: " +
                    delete.StandardError.Trim());
            }
        }
    }

    private static string Slug(string value, int maxLength = 24)
    {
        var chars = value.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length <= maxLength ? slug : slug[..maxLength].TrimEnd('-');
    }
}
