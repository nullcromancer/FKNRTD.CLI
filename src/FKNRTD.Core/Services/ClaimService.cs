using System.Text.RegularExpressions;
using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class ClaimService
{
    private readonly StateStore _store;

    public ClaimService(StateStore store)
    {
        _store = store;
    }

    public async Task<FileClaim> AddAsync(
        string agentId,
        IEnumerable<string> paths,
        ClaimMode mode,
        string worktreePath,
        string? taskId = null,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0)
        {
            throw new ArgumentException(
                "A claim has to name at least one path, because a claim on nothing cannot warn anyone " +
                "about anything. Pass -path once for each file the agent is about to touch.",
                nameof(paths));
        }

        var claim = new FileClaim
        {
            AgentId = agentId,
            TaskId = taskId,
            Mode = mode,
            WorktreePath = Path.GetFullPath(worktreePath),
            Paths = normalized,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(5))
        };
        await _store.SaveClaimAsync(claim, cancellationToken).ConfigureAwait(false);
        return claim;
    }

    public async Task<FileClaim> RenewAsync(
        string claimId,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
    {
        var claim = (await _store.LoadClaimsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id.Equals(claimId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"There is no claim '{claimId}'. It may have expired on its own, which is what claims " +
                "do when they are not renewed. Run 'fknrtd claim list' to see the live ones.");
        claim = claim with
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(5))
        };
        await _store.SaveClaimAsync(claim, cancellationToken).ConfigureAwait(false);
        return claim;
    }

    public void Release(string claimId)
    {
        if (!_store.DeleteClaim(claimId))
        {
            throw new InvalidOperationException(
                $"There is no claim '{claimId}' to release. It may already have expired. Run " +
                "'fknrtd claim list' to see the live ones.");
        }
    }

    public IReadOnlyList<ConflictRecord> Detect(
        IEnumerable<FileClaim> fileClaims,
        IEnumerable<AgentRuntimeState> agentStates,
        DateTimeOffset now)
    {
        var records = new List<ConflictRecord>();
        var claims = fileClaims.ToArray();

        foreach (var stale in claims.Where(claim => claim.ExpiresAt < now))
        {
            records.Add(new ConflictRecord
            {
                Kind = ConflictKind.StaleClaim,
                RiskScore = 25,
                Summary = $"Stale ownership claim from {stale.AgentId}",
                AgentIds = [stale.AgentId],
                Paths = stale.Paths
            });
        }

        var activeClaims = claims.Where(claim => claim.ExpiresAt >= now).ToArray();
        for (var leftIndex = 0; leftIndex < activeClaims.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < activeClaims.Length; rightIndex++)
            {
                AddClaimConflict(activeClaims[leftIndex], activeClaims[rightIndex], records);
            }
        }

        var running = agentStates
            .Where(agent => agent.State is AgentActivityState.Running or AgentActivityState.Planning or
                AgentActivityState.Reviewing)
            .ToArray();
        for (var leftIndex = 0; leftIndex < running.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < running.Length; rightIndex++)
            {
                AddRuntimeConflict(running[leftIndex], running[rightIndex], records);
            }
        }

        return records
            .OrderByDescending(record => record.RiskScore)
            .ThenBy(record => record.Summary, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddClaimConflict(FileClaim left, FileClaim right, ICollection<ConflictRecord> records)
    {
        if (left.AgentId.Equals(right.AgentId, StringComparison.OrdinalIgnoreCase) ||
            left.Mode == ClaimMode.Read && right.Mode == ClaimMode.Read)
        {
            return;
        }

        var overlaps = FindOverlaps(left.Paths, right.Paths);
        if (overlaps.Count == 0)
        {
            return;
        }

        var sameTree = SamePath(left.WorktreePath, right.WorktreePath);
        records.Add(new ConflictRecord
        {
            Kind = sameTree ? ConflictKind.Collision : ConflictKind.MergeRisk,
            RiskScore = sameTree ? 100 : 60,
            Summary = sameTree
                ? $"Live collision: {left.AgentId} and {right.AgentId}"
                : $"Merge risk: {left.AgentId} and {right.AgentId}",
            AgentIds = [left.AgentId, right.AgentId],
            Paths = overlaps
        });
    }

    private static void AddRuntimeConflict(
        AgentRuntimeState left,
        AgentRuntimeState right,
        ICollection<ConflictRecord> records)
    {
        if (left.AgentId.Equals(right.AgentId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var leftPaths = left.TouchedPaths.Concat(left.PlannedPaths).Select(NormalizePath).ToArray();
        var rightPaths = right.TouchedPaths.Concat(right.PlannedPaths).Select(NormalizePath).ToArray();
        var overlaps = FindOverlaps(leftPaths, rightPaths);
        if (overlaps.Count == 0)
        {
            return;
        }

        var sameTree = SamePath(left.Worktree, right.Worktree);
        records.Add(new ConflictRecord
        {
            Kind = sameTree ? ConflictKind.Collision : ConflictKind.MergeRisk,
            RiskScore = sameTree ? 100 : 60,
            Summary = sameTree
                ? $"Live collision: {left.AgentId} and {right.AgentId}"
                : $"Merge risk: {left.AgentId} and {right.AgentId}",
            AgentIds = [left.AgentId, right.AgentId],
            Paths = overlaps
        });
    }

    private static List<string> FindOverlaps(IEnumerable<string> left, IEnumerable<string> right)
    {
        var overlaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var leftPath in left)
        {
            foreach (var rightPath in right)
            {
                if (PatternsOverlap(leftPath, rightPath))
                {
                    overlaps.Add(MoreSpecific(leftPath, rightPath));
                }
            }
        }

        return overlaps.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool PatternsOverlap(string left, string right)
    {
        var normalizedLeft = NormalizePath(left);
        var normalizedRight = NormalizePath(right);
        if (normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!ContainsWildcard(normalizedLeft) && !ContainsWildcard(normalizedRight))
        {
            return normalizedLeft.StartsWith(normalizedRight.TrimEnd('/') + '/', StringComparison.OrdinalIgnoreCase) ||
                   normalizedRight.StartsWith(normalizedLeft.TrimEnd('/') + '/', StringComparison.OrdinalIgnoreCase);
        }

        return GlobMatches(normalizedLeft, normalizedRight) || GlobMatches(normalizedRight, normalizedLeft) ||
               PrefixBeforeWildcard(normalizedLeft).StartsWith(
                   PrefixBeforeWildcard(normalizedRight), StringComparison.OrdinalIgnoreCase) ||
               PrefixBeforeWildcard(normalizedRight).StartsWith(
                   PrefixBeforeWildcard(normalizedLeft), StringComparison.OrdinalIgnoreCase);
    }

    private static bool GlobMatches(string pattern, string candidate)
    {
        if (!ContainsWildcard(pattern))
        {
            return false;
        }

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal)
            .Replace("\\?", "[^/]", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(candidate, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string PrefixBeforeWildcard(string path)
    {
        var wildcard = path.IndexOfAny(['*', '?']);
        return wildcard < 0 ? path : path[..wildcard];
    }

    private static bool ContainsWildcard(string path) => path.IndexOfAny(['*', '?']) >= 0;

    private static string MoreSpecific(string left, string right) => left.Length >= right.Length ? left : right;

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/').TrimEnd('/');
    }

    private static bool SamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), comparison);
    }
}
