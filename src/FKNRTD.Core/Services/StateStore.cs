using System.Text;
using System.Text.Json;
using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class StateStore
{
    private const long DefaultJsonLineFileByteCap = 8 * 1024 * 1024;
    private const int TailReadBufferBytes = 16 * 1024;
    private const int TailReadMaximumBytes = 8 * 1024 * 1024;
    private readonly SemaphoreSlim _appendGate = new(1, 1);
    private readonly long _jsonLineFileByteCap;

    public StateStore(FknrtdPaths paths, long jsonLineFileByteCap = DefaultJsonLineFileByteCap)
    {
        if (jsonLineFileByteCap <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(jsonLineFileByteCap),
                "JSONL file byte cap must be greater than zero.");
        }

        Paths = paths;
        _jsonLineFileByteCap = jsonLineFileByteCap;
    }

    public FknrtdPaths Paths { get; }

    public async Task InitializeAsync(FknrtdConfig config, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Paths.StateRoot);
        Directory.CreateDirectory(Paths.Tasks);
        Directory.CreateDirectory(Paths.Agents);
        Directory.CreateDirectory(Paths.Usage);
        Directory.CreateDirectory(Paths.Claims);
        Directory.CreateDirectory(Paths.Locks);
        Directory.CreateDirectory(Paths.Cancels);
        Directory.CreateDirectory(Paths.Logs);
        Directory.CreateDirectory(Paths.Artifacts);
        Directory.CreateDirectory(Paths.Worktrees);

        await SaveConfigAsync(config, cancellationToken).ConfigureAwait(false);

        var ignorePath = Path.Combine(Paths.StateRoot, ".gitignore");
        if (!File.Exists(ignorePath))
        {
            const string ignore = "runtime/\ntasks/\nlogs/\nworktrees/\nartifacts/\n";
            await File.WriteAllTextAsync(ignorePath, ignore, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task SaveConfigAsync(FknrtdConfig config, CancellationToken cancellationToken = default) =>
        WriteJsonAtomicAsync(Paths.Config, config, cancellationToken);

    public Task<FknrtdConfig> LoadConfigAsync(CancellationToken cancellationToken = default) =>
        ReadJsonRequiredAsync<FknrtdConfig>(Paths.Config, cancellationToken);

    public Task SaveTaskAsync(WorkflowTask task, CancellationToken cancellationToken = default) =>
        WriteJsonAtomicAsync(TaskPath(task.Id), task, cancellationToken);

    public Task<WorkflowTask> LoadTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
        ReadJsonRequiredAsync<WorkflowTask>(TaskPath(taskId), cancellationToken);

    public Task<IReadOnlyList<WorkflowTask>> LoadTasksAsync(CancellationToken cancellationToken = default) =>
        ReadDirectoryAsync<WorkflowTask>(Paths.Tasks, "*.json", cancellationToken);

    public Task SaveAgentRuntimeAsync(AgentRuntimeState state, CancellationToken cancellationToken = default) =>
        WriteJsonAtomicAsync(Path.Combine(Paths.Agents, SafeName(state.AgentId) + ".json"), state, cancellationToken);

    public Task<IReadOnlyList<AgentRuntimeState>> LoadAgentRuntimesAsync(CancellationToken cancellationToken = default) =>
        ReadDirectoryAsync<AgentRuntimeState>(Paths.Agents, "*.json", cancellationToken);

    public Task SaveUsageAsync(UsageSnapshot snapshot, CancellationToken cancellationToken = default) =>
        WriteJsonAtomicAsync(Path.Combine(Paths.Usage, SafeName(snapshot.AgentId) + ".json"), snapshot, cancellationToken);

    public Task<IReadOnlyList<UsageSnapshot>> LoadUsageAsync(CancellationToken cancellationToken = default) =>
        ReadDirectoryAsync<UsageSnapshot>(Paths.Usage, "*.json", cancellationToken);

    public Task SaveClaimAsync(FileClaim claim, CancellationToken cancellationToken = default) =>
        WriteJsonAtomicAsync(Path.Combine(Paths.Claims, SafeName(claim.Id) + ".json"), claim, cancellationToken);

    public Task<IReadOnlyList<FileClaim>> LoadClaimsAsync(CancellationToken cancellationToken = default) =>
        ReadDirectoryAsync<FileClaim>(Paths.Claims, "*.json", cancellationToken);

    public bool DeleteClaim(string claimId)
    {
        var path = Path.Combine(Paths.Claims, SafeName(claimId) + ".json");
        if (File.Exists(path))
        {
            File.Delete(path);
            return true;
        }

        return false;
    }

    public async Task AppendEventAsync(FknrtdEvent item, CancellationToken cancellationToken = default) =>
        await AppendJsonLineAsync(Paths.Events, item, cancellationToken).ConfigureAwait(false);

    public async Task AppendMessageAsync(AgentMessage item, CancellationToken cancellationToken = default) =>
        await AppendJsonLineAsync(Paths.Messages, item, cancellationToken).ConfigureAwait(false);

    public Task<IReadOnlyList<FknrtdEvent>> LoadEventsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        ReadJsonLinesAsync<FknrtdEvent>(Paths.Events, limit, cancellationToken);

    public Task<IReadOnlyList<AgentMessage>> LoadMessagesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        ReadJsonLinesAsync<AgentMessage>(Paths.Messages, limit, cancellationToken);

    public string TaskLogPath(string taskId, WorkflowStage stage, int attempt = 0)
    {
        var suffix = attempt > 0 ? $"-{attempt}" : string.Empty;
        return Path.Combine(Paths.Logs, SafeName(taskId), $"{stage.ToString().ToLowerInvariant()}{suffix}.log");
    }

    public string TaskArtifactPath(string taskId, string fileName) =>
        Path.Combine(Paths.Artifacts, SafeName(taskId), SafeName(fileName, preserveExtension: true));

    public string TaskPath(string taskId) => Path.Combine(Paths.Tasks, SafeName(taskId) + ".json");

    public string CancelPath(string taskId) => Path.Combine(Paths.Cancels, SafeName(taskId) + ".cancel");

    public async Task<ExclusiveFileLease> AcquireTaskLeaseAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Paths.Locks, SafeName(taskId) + ".lock");
        return await ExclusiveFileLease.AcquireAsync(
                path,
                "This task is already being run by another FKNRTD.CLI process.",
                maximumAttempts: 20,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ExclusiveFileLease> AcquireAgentLeaseAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Paths.Locks, "agent-" + SafeName(agentId) + ".lock");
        return await ExclusiveFileLease.AcquireAsync(
                path,
                $"Agent '{agentId}' is busy.",
                maximumAttempts: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static string SafeName(string value, bool preserveExtension = false)
    {
        var source = preserveExtension ? value : Path.GetFileNameWithoutExtension(value);
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(source.Select(character =>
            invalid.Contains(character) || character is '/' or '\\' ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unnamed" : safe;
    }

    private static async Task WriteJsonAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Unable to resolve the directory for {path}.");
        Directory.CreateDirectory(directory);

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonSupport.Options, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // The temporary file may be held briefly by antivirus software; it is safe to leave behind.
            }
            catch (UnauthorizedAccessException)
            {
                // The original exception remains more useful than a cleanup failure.
            }
        }
    }

    private static async Task<T> ReadJsonRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required FKNRTD.CLI state file was not found: {path}", path);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonSupport.Options, cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidDataException($"The JSON file is empty or invalid: {path}");
    }

    private static async Task<IReadOnlyList<T>> ReadDirectoryAsync<T>(
        string directory,
        string searchPattern,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var results = new List<T>();
        foreach (var path in Directory.EnumerateFiles(directory, searchPattern).OrderBy(path => path))
        {
            try
            {
                results.Add(await ReadJsonRequiredAsync<T>(path, cancellationToken).ConfigureAwait(false));
            }
            catch (IOException)
            {
                // A writer may be replacing this snapshot. The next refresh will pick it up.
            }
            catch (JsonException)
            {
                // A corrupt optional runtime snapshot must not take down the command center.
            }
        }

        return results;
    }

    private async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(value, JsonSupport.CompactOptions) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(line);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await _appendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    RotateJsonLineFileIfNeeded(path, bytes.Length);
                    await using var stream = new FileStream(
                        path,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read | FileShare.Delete,
                        16 * 1024,
                        FileOptions.Asynchronous | FileOptions.WriteThrough);
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (IOException) when (attempt < 10)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(15 * (attempt + 1)), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _appendGate.Release();
        }
    }

    private static async Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(
        string path,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0 || (!File.Exists(path) && !File.Exists(RotationPath(path))))
        {
            return [];
        }

        try
        {
            var current = await ReadJsonLineTailAsync<T>(path, limit, cancellationToken).ConfigureAwait(false);
            if (current.Count >= limit)
            {
                return current;
            }

            var archive = await ReadJsonLineTailAsync<T>(
                    RotationPath(path),
                    limit - current.Count,
                    cancellationToken)
                .ConfigureAwait(false);
            if (archive.Count == 0)
            {
                return current;
            }

            return archive.Concat(current).ToArray();
        }
        catch (IOException)
        {
            // Rotation or a concurrent appender can briefly replace the active file.
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            // A transient sharing restriction must not take down dashboard refresh.
            return [];
        }
    }

    private void RotateJsonLineFileIfNeeded(string path, int incomingBytes)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var length = new FileInfo(path).Length;
        if (length == 0 || length + incomingBytes <= _jsonLineFileByteCap)
        {
            return;
        }

        File.Move(path, RotationPath(path), overwrite: true);
    }

    private static async Task<IReadOnlyList<T>> ReadJsonLineTailAsync<T>(
        string path,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || limit <= 0)
        {
            return [];
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            TailReadBufferBytes,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        var position = stream.Length;
        var newlineCount = 0;
        var scannedBytes = 0;
        var chunks = new List<byte[]>();
        while (position > 0 && newlineCount <= limit && scannedBytes < TailReadMaximumBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(
                Math.Min(TailReadBufferBytes, position),
                TailReadMaximumBytes - scannedBytes);
            position -= requested;
            stream.Position = position;
            var buffer = new byte[requested];
            var read = 0;
            while (read < requested)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(read, requested - read), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            if (read != buffer.Length)
            {
                Array.Resize(ref buffer, read);
            }

            newlineCount += buffer.Count(value => value == (byte)'\n');
            scannedBytes += buffer.Length;
            chunks.Add(buffer);
        }

        var byteCount = chunks.Sum(chunk => chunk.Length);
        var bytes = new byte[byteCount];
        var destination = 0;
        for (var index = chunks.Count - 1; index >= 0; index--)
        {
            chunks[index].CopyTo(bytes, destination);
            destination += chunks[index].Length;
        }

        var lines = Encoding.UTF8.GetString(bytes)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new List<T>(Math.Min(limit, lines.Length));
        for (var index = lines.Length - 1; index >= 0 && results.Count < limit; index--)
        {
            try
            {
                var item = JsonSerializer.Deserialize<T>(lines[index], JsonSupport.Options);
                if (item is not null)
                {
                    results.Add(item);
                }
            }
            catch (JsonException)
            {
                // Ignore a partial or malformed record while retaining valid recent history.
            }
        }

        results.Reverse();
        return results;
    }

    private static string RotationPath(string path) => path + ".1";
}

public sealed class ExclusiveFileLease : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan DefaultAcquisitionTimeout = TimeSpan.FromSeconds(5);
    private readonly FileStream _stream;
    private bool _disposed;

    private ExclusiveFileLease(string path, FileStream stream)
    {
        Path = path;
        _stream = stream;
    }

    public string Path { get; }

    public static async Task<ExclusiveFileLease> AcquireAsync(
        string path,
        string busyMessage,
        int? maximumAttempts,
        TimeSpan? acquisitionTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (maximumAttempts is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        var timeout = acquisitionTimeout ?? DefaultAcquisitionTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(acquisitionTimeout));
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.Read | FileShare.Delete);
                try
                {
                    stream.SetLength(0);
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
                    await writer.WriteAsync(
                            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Position = 0;
                    return new ExclusiveFileLease(path, stream);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch (IOException exception)
            {
                var attemptsExhausted = maximumAttempts is not null && attempt >= maximumAttempts.Value;
                var timeExhausted = elapsed.Elapsed >= timeout;
                if (attemptsExhausted || timeExhausted)
                {
                    var owner = ReadLockOwner(path);
                    throw new InvalidOperationException(
                        $"{busyMessage} Lock held by PID {owner}.", exception);
                }

                var remaining = timeout - elapsed.Elapsed;
                var delay = remaining < TimeSpan.FromMilliseconds(100)
                    ? remaining
                    : TimeSpan.FromMilliseconds(100);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // Another process may already be acquiring or cleaning up this lock path.
        }
        catch (UnauthorizedAccessException)
        {
            // Lock release still succeeded even if stale-file cleanup was denied.
        }
        finally
        {
            _stream.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static string ReadLockOwner(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var owner = reader.ReadToEnd().Trim().TrimStart('\uFEFF');
            return owner.Length == 0 ? "unknown" : owner;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unknown";
        }
    }
}
