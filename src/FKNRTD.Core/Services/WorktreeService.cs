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
        CancellationToken cancellationToken = default)
    {
        if (!await _git.IsRepositoryAsync(_store.Paths.Root, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("FKNRTD.CLI worktree isolation requires a Git repository.");
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
            throw new InvalidOperationException("Unable to create the isolated worktree: " + result.StandardError.Trim());
        }

        return (path, branch);
    }

    public async Task<CommandResult> LandAsync(
        WorkflowTask task,
        bool requireCleanTree,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _git.GetSnapshotAsync(_store.Paths.Root, cancellationToken).ConfigureAwait(false);
        var baseBranch = await _git.ResolveBaseBranchAsync(
                _store.Paths.Root,
                task.BaseRef,
                cancellationToken)
            .ConfigureAwait(false);
        task.BaseRef = baseBranch;
        if (requireCleanTree && !snapshot.IsClean)
        {
            throw new InvalidOperationException("Landing is blocked because the primary worktree has uncommitted changes.");
        }

        if (!string.Equals(snapshot.Branch, baseBranch, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Landing is blocked because the primary worktree is on '{snapshot.Branch}', not '{baseBranch}'.");
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

    public async Task RemoveAsync(WorkflowTask task, bool force, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(task.WorktreePath) && Directory.Exists(task.WorktreePath))
        {
            var arguments = force
                ? new[] { "worktree", "remove", "--force", task.WorktreePath }
                : new[] { "worktree", "remove", task.WorktreePath };
            var remove = await _git.GitAsync(_store.Paths.Root, arguments, cancellationToken).ConfigureAwait(false);
            if (!remove.Success)
            {
                throw new InvalidOperationException("Unable to remove the worktree: " + remove.StandardError.Trim());
            }
        }

        var prune = await _git.GitAsync(_store.Paths.Root, ["worktree", "prune"], cancellationToken)
            .ConfigureAwait(false);
        if (!prune.Success)
        {
            throw new InvalidOperationException("Unable to prune stale worktree state: " + prune.StandardError.Trim());
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
                throw new InvalidOperationException("Unable to delete the landed task branch: " +
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
