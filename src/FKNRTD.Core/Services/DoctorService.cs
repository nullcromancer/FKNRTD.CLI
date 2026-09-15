using System.Runtime.InteropServices;
using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class DoctorService
{
    private readonly StateStore _store;
    private readonly GitService _git;
    private readonly ProcessRunner _process;

    public DoctorService(StateStore store, GitService git, ProcessRunner process)
    {
        _store = store;
        _git = git;
        _process = process;
    }

    public async Task<IReadOnlyList<DoctorCheck>> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<DoctorCheck>
        {
            new()
            {
                Name = ".NET runtime",
                Passed = Environment.Version.Major >= 10,
                Detail = $"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription}"
            },
            new()
            {
                Name = "Configuration",
                Passed = File.Exists(_store.Paths.Config),
                Detail = _store.Paths.Config
            }
        };

        checks.Add(new DoctorCheck
        {
            Name = "Git executable",
            Passed = ExecutableLocator.Find("git") is not null,
            Detail = ExecutableLocator.Find("git") ?? "Not found"
        });
        checks.Add(new DoctorCheck
        {
            Name = "Git repository",
            Passed = await _git.IsRepositoryAsync(_store.Paths.Root, cancellationToken).ConfigureAwait(false),
            Detail = _store.Paths.Root
        });

        var config = await _store.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        foreach (var agent in config.Agents)
        {
            var executable = ExecutableLocator.Find(agent.Executable);
            var detail = executable ?? "Not found on PATH";
            if (executable is not null)
            {
                var version = await GetVersionAsync(executable, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    detail += $" ({version})";
                }
            }

            checks.Add(new DoctorCheck
            {
                Name = $"Agent: {agent.DisplayName}",
                Passed = executable is not null || !agent.Enabled,
                Required = agent.Enabled,
                Detail = agent.Enabled ? detail : detail + " [disabled]"
            });
        }

        var writable = false;
        var probe = Path.Combine(_store.Paths.Runtime, $"write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(probe, "ok", cancellationToken).ConfigureAwait(false);
            File.Delete(probe);
            writable = true;
        }
        catch (IOException)
        {
            writable = false;
        }
        catch (UnauthorizedAccessException)
        {
            writable = false;
        }

        checks.Add(new DoctorCheck
        {
            Name = "State directory writable",
            Passed = writable,
            Detail = _store.Paths.StateRoot
        });
        return checks;
    }

    private async Task<string> GetVersionAsync(string executable, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _process.RunAsync(
                    executable,
                    ["--version"],
                    _store.Paths.Root,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var text = (result.StandardOutput + " " + result.StandardError).Trim();
            return text.Length <= 100 ? text : text[..100];
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
