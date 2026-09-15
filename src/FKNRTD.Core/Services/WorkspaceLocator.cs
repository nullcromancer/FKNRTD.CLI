namespace FKNRTD.Services;

public sealed record FknrtdPaths(string Root)
{
    public string StateRoot => Path.Combine(Root, ".fknrtd");
    public string Config => Path.Combine(StateRoot, "config.json");
    public string Tasks => Path.Combine(StateRoot, "tasks");
    public string Runtime => Path.Combine(StateRoot, "runtime");
    public string Agents => Path.Combine(Runtime, "agents");
    public string Usage => Path.Combine(Runtime, "usage");
    public string Claims => Path.Combine(Runtime, "claims");
    public string Locks => Path.Combine(Runtime, "locks");
    public string Cancels => Path.Combine(Runtime, "cancels");
    public string Logs => Path.Combine(StateRoot, "logs");
    public string Artifacts => Path.Combine(StateRoot, "artifacts");
    public string Worktrees => Path.Combine(StateRoot, "worktrees");
    public string Events => Path.Combine(Runtime, "events.jsonl");
    public string Messages => Path.Combine(Runtime, "messages.jsonl");
}

public static class WorkspaceLocator
{
    public static FknrtdPaths Find(string? start = null)
    {
        var current = new DirectoryInfo(Path.GetFullPath(start ?? Environment.CurrentDirectory));
        while (current is not null)
        {
            var config = Path.Combine(current.FullName, ".fknrtd", "config.json");
            if (File.Exists(config))
            {
                return new FknrtdPaths(ResolvePrimaryWorktree(current.FullName));
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "No FKNRTD.CLI project was found. Run 'fknrtd init' from the repository root.");
    }

    public static FknrtdPaths ForRoot(string root) => new(Path.GetFullPath(root));

    private static string ResolvePrimaryWorktree(string candidate)
    {
        var marker = Path.Combine(candidate, ".git");
        if (!File.Exists(marker))
        {
            return candidate;
        }

        try
        {
            var pointer = File.ReadAllText(marker).Trim();
            if (!pointer.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            var gitDirectory = Path.GetFullPath(pointer[7..].Trim(), candidate);
            var commonPointer = Path.Combine(gitDirectory, "commondir");
            if (!File.Exists(commonPointer))
            {
                return candidate;
            }

            var commonDirectory = Path.GetFullPath(File.ReadAllText(commonPointer).Trim(), gitDirectory);
            if (!Path.GetFileName(commonDirectory).Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            var primary = Directory.GetParent(commonDirectory)?.FullName;
            return primary is not null && File.Exists(Path.Combine(primary, ".fknrtd", "config.json"))
                ? primary
                : candidate;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return candidate;
        }
    }
}
