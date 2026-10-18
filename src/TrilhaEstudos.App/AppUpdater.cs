using Velopack;
using Velopack.Sources;

namespace TrilhaEstudos.App;

public sealed record AppRelease(string Version, string Notes);

public interface IAppUpdater
{
    bool IsSupported { get; }
    string UnsupportedReason { get; }
    Task<AppRelease?> CheckAsync(CancellationToken token);
    Task DownloadAsync(AppRelease release, IProgress<int> progress, CancellationToken token);
    void ApplyAndRestart(AppRelease release);
}

public sealed class UnavailableAppUpdater : IAppUpdater
{
    public bool IsSupported => false;
    public string UnsupportedReason => "Para receber atualizações, use a versão instalada pelo instalador oficial.";
    public Task<AppRelease?> CheckAsync(CancellationToken token) => Task.FromResult<AppRelease?>(null);
    public Task DownloadAsync(AppRelease release, IProgress<int> progress, CancellationToken token) => throw new InvalidOperationException(UnsupportedReason);
    public void ApplyAndRestart(AppRelease release) => throw new InvalidOperationException(UnsupportedReason);
}

public sealed class VelopackAppUpdater : IAppUpdater
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _candidate;
    private string? _downloaded;
    public bool IsSupported => _manager?.IsInstalled == true;
    public string UnsupportedReason { get; }

    public VelopackAppUpdater(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com")
        {
            UnsupportedReason = "Esta compilação de desenvolvimento não tem um repositório de atualizações configurado.";
            return;
        }
        _manager = new UpdateManager(new GithubSource(repositoryUrl, accessToken: null, prerelease: false),
            new UpdateOptions { AllowVersionDowngrade = false, ExplicitChannel = "win-x64" });
        UnsupportedReason = "Para receber atualizações, use a versão instalada pelo instalador oficial.";
    }

    // O construtor interno permite testar pacotes reais com um feed e um localizador isolados.
    internal VelopackAppUpdater(UpdateManager manager) { _manager = manager; UnsupportedReason = "Instalação de teste indisponível."; }

    public async Task<AppRelease?> CheckAsync(CancellationToken token)
    {
        if (!IsSupported) throw new InvalidOperationException(UnsupportedReason);
        _candidate = null; _downloaded = null;
        var update = await _manager!.CheckForUpdatesAsync().WaitAsync(token);
        token.ThrowIfCancellationRequested();
        var version = update?.TargetFullRelease.Version.ToString();
        if (!Version.TryParse(version, out var number) || number <= AppVersion.Number || update!.IsDowngrade) return null;
        _candidate = update;
        return new AppRelease(number.ToString(3), update.TargetFullRelease.NotesMarkdown ?? "");
    }

    public async Task DownloadAsync(AppRelease release, IProgress<int> progress, CancellationToken token)
    {
        var candidate = RequireCandidate(release);
        _downloaded = null;
        await _manager!.DownloadUpdatesAsync(candidate, value => progress.Report(value), token);
        token.ThrowIfCancellationRequested();
        _downloaded = release.Version;
    }

    public void ApplyAndRestart(AppRelease release)
    {
        var candidate = RequireCandidate(release);
        if (_downloaded != release.Version) throw new InvalidOperationException("Baixe e valide a atualização antes de instalar.");
        _manager!.WaitExitThenApplyUpdates(candidate.TargetFullRelease, silent: true, restart: true,
            restartArgs: Environment.GetCommandLineArgs().Skip(1).ToArray());
        System.Windows.Application.Current.Shutdown();
    }

    private UpdateInfo RequireCandidate(AppRelease release)
    {
        if (!IsSupported || _candidate == null || _candidate.TargetFullRelease.Version.ToString() != release.Version
            || !Version.TryParse(release.Version, out var version) || version <= AppVersion.Number)
            throw new InvalidOperationException("Verifique novamente as atualizações antes de continuar.");
        return _candidate;
    }
}
