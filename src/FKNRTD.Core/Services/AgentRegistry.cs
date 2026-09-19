using FKNRTD.Domain;

namespace FKNRTD.Services;

/// <summary>Owns configuration edits. Every change starts from fresh state under one workspace lease.</summary>
public sealed class AgentRegistry(StateStore store)
{
    /// <summary>Applies a setting to the configuration read inside the lease, returning the saved value.</summary>
    public async Task<FknrtdConfig> UpdateAsync(
        Func<FknrtdConfig, FknrtdConfig> change, CancellationToken cancellationToken = default)
    {
        await using var lease = await store.AcquireConfigLeaseAsync(cancellationToken).ConfigureAwait(false);
        var config = await store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        var updated = change(config);
        if (!ReferenceEquals(config, updated))
        {
            await store.SaveConfigAsync(updated, cancellationToken).ConfigureAwait(false);
        }

        return updated;
    }

    public async Task AddAsync(AgentDefinition agent, CancellationToken cancellationToken = default)
    {
        if (!await TryAddAsync(agent, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Agent '{agent.Id}' is already configured.");
        }
    }

    public async Task<bool> TryAddAsync(AgentDefinition agent, CancellationToken cancellationToken = default)
    {
        var added = false;
        await UpdateAsync(config =>
        {
            ValidateAgentDefinition(agent);
            if (config.Agents.Any(item => Matches(item, agent.Id)))
            {
                return config;
            }

            added = true;
            return config with { Agents = config.Agents.Append(agent).ToList() };
        }, cancellationToken).ConfigureAwait(false);
        return added;
    }

    public Task<FknrtdConfig> SetAsync(string id, string? executable, string? displayName,
        bool requireExisting = true, CancellationToken cancellationToken = default) =>
        UpdateAsync(config =>
        {
            if (requireExisting && !config.Agents.Any(agent => Matches(agent, id)))
            {
                throw new InvalidOperationException(
                    $"Agent '{id}' is not configured. 'fknrtd agent list' shows what is, and " +
                    "'fknrtd agent add' creates a new one.");
            }

            return config with
            {
                Agents = config.Agents.Select(agent => Matches(agent, id)
                    ? agent with
                    {
                        Executable = executable ?? agent.Executable,
                        DisplayName = displayName ?? agent.DisplayName
                    }
                    : agent).ToList()
            };
        }, cancellationToken);

    public Task<FknrtdConfig> SetEnabledAsync(string id, bool enabled,
        CancellationToken cancellationToken = default) => UpdateAsync(config =>
        {
            if (!config.Agents.Any(agent => Matches(agent, id)))
            {
                throw new InvalidOperationException($"Agent '{id}' is not configured.");
            }

            return config with
            {
                Agents = config.Agents.Select(agent => Matches(agent, id)
                    ? agent with { Enabled = enabled } : agent).ToList()
            };
        }, cancellationToken);

    /// <summary>Returns the new flag, or null if the agent was removed while the roster was open.</summary>
    public async Task<bool?> ToggleAsync(string id, CancellationToken cancellationToken = default)
    {
        bool? enabled = null;
        await UpdateAsync(config =>
        {
            var existing = config.Agents.FirstOrDefault(agent => Matches(agent, id));
            if (existing is null)
            {
                return config;
            }

            enabled = !existing.Enabled;
            return config with
            {
                Agents = config.Agents.Select(agent => Matches(agent, id)
                    ? agent with { Enabled = enabled.Value } : agent).ToList()
            };
        }, cancellationToken).ConfigureAwait(false);
        return enabled;
    }

    public Task<FknrtdConfig> RemoveAsync(string id, bool requireExisting = true,
        CancellationToken cancellationToken = default) => UpdateAsync(config =>
        {
            var agents = config.Agents.Where(agent => !Matches(agent, id)).ToList();
            if (requireExisting && agents.Count == config.Agents.Count)
            {
                throw new InvalidOperationException($"Agent '{id}' is not configured.");
            }

            return config with { Agents = agents };
        }, cancellationToken);

    private static bool Matches(AgentDefinition agent, string id) =>
        agent.Id.Equals(id, StringComparison.OrdinalIgnoreCase);

    public static void ValidateAgentDefinition(AgentDefinition agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Id) || string.IsNullOrWhiteSpace(agent.Executable))
        {
            throw new InvalidDataException("Each agent requires non-empty id and executable values.");
        }

        if (agent.Id.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidDataException($"Agent ID '{agent.Id}' can contain only letters, numbers, hyphens, and underscores.");
        }

        if (agent.Profiles.Count == 0)
        {
            throw new InvalidDataException($"Agent '{agent.Id}' needs at least one command profile.");
        }
    }
}
