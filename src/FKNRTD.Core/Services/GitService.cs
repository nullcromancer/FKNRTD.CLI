using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class GitService
{
    private readonly ProcessRunner _processRunner;

    public GitService(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    /// <summary>
    /// Whether <c>git</c> was found on PATH. Remembered once it has been, because the dashboard
    /// takes a snapshot every second and each probe walks every PATH directory against every
    /// executable extension - well over a hundred file checks on Windows, repeated forever, to
    /// answer a question whose answer does not change.
    /// </summary>
    /// <remarks>
    /// Only the positive answer is remembered. Git appearing mid-session is the case worth
    /// noticing, and it is noticed on the next probe; git disappearing mid-session is not, and
    /// would surface as a real error at the point something tried to run it.
    /// </remarks>
    public static bool IsInstalled()
    {
        if (_gitFound)
        {
            return true;
        }

        try
        {
            _gitFound = ExecutableLocator.Find("git") is not null;
            return _gitFound;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool _gitFound;

    public async Task<bool> IsRepositoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!IsInstalled())
        {
            return false;
        }

        var result = await GitAsync(directory, ["rev-parse", "--is-inside-work-tree"], cancellationToken)
            .ConfigureAwait(false);
        return result.Success && result.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<GitSnapshot> GetSnapshotAsync(string directory, CancellationToken cancellationToken = default)
    {
        var rootResult = IsInstalled()
            ? await GitAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false)
            : null;
        if (rootResult is null || !rootResult.Success)
        {
            return new GitSnapshot
            {
                RepositoryName = new DirectoryInfo(directory).Name,
                RepositoryRoot = Path.GetFullPath(directory)
            };
        }

        var root = rootResult.StandardOutput.Trim();
        var branchResult = await GitAsync(root, ["branch", "--show-current"], cancellationToken)
            .ConfigureAwait(false);
        var remoteResult = await GitAsync(root, ["remote", "get-url", "origin"], cancellationToken)
            .ConfigureAwait(false);
        var statusResult = await GitAsync(root, ["status", "--porcelain=v1", "-z"], cancellationToken)
            .ConfigureAwait(false);
        var divergenceResult = await GitAsync(
                root,
                ["rev-list", "--left-right", "--count", "HEAD...@{upstream}"],
                cancellationToken)
            .ConfigureAwait(false);

        var (ahead, behind) = ParseDivergence(divergenceResult.StandardOutput);
        var remote = remoteResult.Success ? remoteResult.StandardOutput.Trim() : string.Empty;

        return new GitSnapshot
        {
            RepositoryName = ParseRepositoryName(remote, root),
            RepositoryRoot = root,
            IsRepository = true,
            Branch = branchResult.Success ? branchResult.StandardOutput.Trim() : "detached",
            Remote = remote,
            ChangedFiles = statusResult.Success ? ParseStatusPaths(statusResult.StandardOutput).Count : 0,
            Ahead = ahead,
            Behind = behind
        };
    }

    public async Task<IReadOnlyList<string>> GetChangedPathsAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var result = await GitAsync(directory, ["status", "--porcelain=v1", "-z", "-uall"], cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return [];
        }

        return ParseStatusPaths(result.StandardOutput)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> GetDiffPathsAsync(
        string directory,
        string baseRef,
        CancellationToken cancellationToken = default)
    {
        var committed = await GitAsync(
                directory,
                ["diff", "--name-only", "-z", $"{baseRef}...HEAD"],
                cancellationToken)
            .ConfigureAwait(false);
        var uncommitted = await GetChangedPathsAsync(directory, cancellationToken).ConfigureAwait(false);
        var paths = committed.Success ? ParseNullTerminated(committed.StandardOutput) : [];
        return paths.Concat(uncommitted)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// The finished change for a task: everything committed on top of the base branch, plus anything
    /// still uncommitted in the worktree. Both halves matter — a workspace that does not commit agent
    /// changes automatically has the entire change sitting uncommitted, and showing only the
    /// committed half would report an empty diff for work that is plainly there.
    /// </summary>
    /// <param name="maximumLines">
    /// A ceiling on what is returned. A diff is unbounded and the caller is a terminal; a very large
    /// change is reported as truncated rather than read entirely into memory.
    /// </param>
    public async Task<(IReadOnlyList<string> Lines, bool Truncated)> GetDiffAsync(
        string directory,
        string baseRef,
        int maximumLines = 4000,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseRef))
        {
            var committed = await GitAsync(
                    directory,
                    ["diff", "--no-color", $"{baseRef}...HEAD"],
                    cancellationToken)
                .ConfigureAwait(false);
            if (committed.Success)
            {
                lines.AddRange(SplitLines(committed.StandardOutput));
            }
        }

        var pending = await GitAsync(directory, ["diff", "--no-color", "HEAD"], cancellationToken)
            .ConfigureAwait(false);
        if (pending.Success)
        {
            var uncommitted = SplitLines(pending.StandardOutput);
            if (uncommitted.Count > 0)
            {
                if (lines.Count > 0)
                {
                    lines.Add(string.Empty);
                }

                lines.Add("--- uncommitted in the worktree ---");
                lines.AddRange(uncommitted);
            }
        }

        return lines.Count > maximumLines
            ? (lines.Take(maximumLines).ToArray(), true)
            : (lines, false);
    }

    private static List<string> SplitLines(string output) =>
        output.Split([(char)13, (char)10], StringSplitOptions.RemoveEmptyEntries).ToList();

    public async Task<string> GetHeadAsync(string directory, CancellationToken cancellationToken = default)
    {
        var result = await GitAsync(directory, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Git could not resolve the current commit, so there is nothing to branch a task from. " +
                "This usually means the repository has no commits yet: make one, then try again. Git " +
                "said: " + result.StandardError.Trim());
        }

        return result.StandardOutput.Trim();
    }

    public async Task<string> ResolveBaseBranchAsync(
        string directory,
        string? baseRef = null,
        CancellationToken cancellationToken = default)
    {
        var requested = baseRef?.Trim();
        if (string.IsNullOrWhiteSpace(requested) || requested.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            var current = await GitAsync(directory, ["branch", "--show-current"], cancellationToken)
                .ConfigureAwait(false);
            var branch = current.StandardOutput.Trim();
            if (!current.Success || branch.Length == 0)
            {
                throw new InvalidOperationException(
                    "This checkout has a detached HEAD, so there is no branch for a task to start from " +
                    "and merge back into. Check out a branch, or name one explicitly when creating the " +
                    "task.");
            }

            return branch;
        }

        const string localPrefix = "refs/heads/";
        var branchName = requested.StartsWith(localPrefix, StringComparison.Ordinal)
            ? requested[localPrefix.Length..]
            : requested;
        if (!await BranchExistsAsync(directory, branchName, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"'{requested}' is not a local branch in this repository, so a task cannot start from it. " +
                "Check the spelling, or create the branch first. 'git branch' lists what exists.");
        }

        return branchName;
    }

    public async Task<bool> BranchExistsAsync(
        string directory,
        string branch,
        CancellationToken cancellationToken = default)
    {
        var result = await GitAsync(
                directory,
                ["show-ref", "--verify", "--quiet", $"refs/heads/{branch}"],
                cancellationToken)
            .ConfigureAwait(false);
        return result.Success;
    }

    public Task<CommandResult> GitAsync(
        string directory,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default) =>
        _processRunner.RunAsync(
            "git",
            new[] { "-c", "core.quotePath=false" }.Concat(arguments),
            directory,
            cancellationToken: cancellationToken);

    private static (int Ahead, int Behind) ParseDivergence(string output)
    {
        var parts = output.Split(['\t', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out var ahead) || !int.TryParse(parts[1], out var behind))
        {
            return (0, 0);
        }

        return (ahead, behind);
    }

    private static string ParseRepositoryName(string remote, string root)
    {
        if (string.IsNullOrWhiteSpace(remote))
        {
            return new DirectoryInfo(root).Name;
        }

        var cleaned = remote.TrimEnd('/');
        if (cleaned.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^4];
        }

        if (cleaned.Contains(':') && !cleaned.Contains("://", StringComparison.Ordinal))
        {
            cleaned = cleaned[(cleaned.IndexOf(':') + 1)..];
        }
        else if (Uri.TryCreate(cleaned, UriKind.Absolute, out var uri))
        {
            cleaned = uri.AbsolutePath.Trim('/');
        }

        var segments = cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? string.Join('/', segments.TakeLast(2)) : segments.LastOrDefault() ?? cleaned;
    }

    private static IReadOnlyList<string> ParseStatusPaths(string output)
    {
        var records = ParseNullTerminated(output);
        var paths = new List<string>(records.Count);
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.Length < 3)
            {
                continue;
            }

            var statusX = record[0];
            var statusY = record[1];
            paths.Add(record[3..]);
            if ((statusX is 'R' or 'C' || statusY is 'R' or 'C') && index + 1 < records.Count)
            {
                index++;
            }
        }

        return paths;
    }

    private static IReadOnlyList<string> ParseNullTerminated(string output)
    {
        var lastNull = output.LastIndexOf('\0');
        if (lastNull >= 0 && output[(lastNull + 1)..].All(character => character is '\r' or '\n'))
        {
            output = output[..(lastNull + 1)];
        }

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
