using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class GitService
{
    private readonly ProcessRunner _processRunner;

    public GitService(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<bool> IsRepositoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        var result = await GitAsync(directory, ["rev-parse", "--is-inside-work-tree"], cancellationToken)
            .ConfigureAwait(false);
        return result.Success && result.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<GitSnapshot> GetSnapshotAsync(string directory, CancellationToken cancellationToken = default)
    {
        var rootResult = await GitAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken)
            .ConfigureAwait(false);
        if (!rootResult.Success)
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
        var statusResult = await GitAsync(root, ["status", "--porcelain=v1"], cancellationToken)
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
            Branch = branchResult.Success ? branchResult.StandardOutput.Trim() : "detached",
            Remote = remote,
            ChangedFiles = statusResult.Success
                ? statusResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
                : 0,
            Ahead = ahead,
            Behind = behind
        };
    }

    public async Task<IReadOnlyList<string>> GetChangedPathsAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var result = await GitAsync(directory, ["status", "--porcelain=v1", "-uall"], cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return [];
        }

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseStatusPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
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
                ["diff", "--name-only", $"{baseRef}...HEAD"],
                cancellationToken)
            .ConfigureAwait(false);
        var uncommitted = await GetChangedPathsAsync(directory, cancellationToken).ConfigureAwait(false);
        var paths = committed.Success
            ? committed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();
        return paths.Concat(uncommitted)
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string> GetHeadAsync(string directory, CancellationToken cancellationToken = default)
    {
        var result = await GitAsync(directory, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException("Unable to resolve the current Git commit: " + result.StandardError.Trim());
        }

        return result.StandardOutput.Trim();
    }

    public async Task<string> ResolveBaseBranchAsync(string directory, CancellationToken cancellationToken = default)
    {
        var result = await GitAsync(directory, ["branch", "--show-current"], cancellationToken).ConfigureAwait(false);
        var branch = result.StandardOutput.Trim();
        return result.Success && branch.Length > 0 ? branch : "HEAD";
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
        _processRunner.RunAsync("git", arguments, directory, cancellationToken: cancellationToken);

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

    private static string ParseStatusPath(string line)
    {
        if (line.Length <= 3)
        {
            return line;
        }

        var path = line[3..].Trim();
        var rename = path.LastIndexOf(" -> ", StringComparison.Ordinal);
        if (rename >= 0)
        {
            path = path[(rename + 4)..];
        }

        return path.Trim('"').Replace('\\', '/');
    }
}
