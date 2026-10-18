using System.Net.Http;
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
    private readonly GithubLatestReleaseClient? _catalog;
    private readonly Func<IUpdateSource, UpdateManager>? _createManager;
    private readonly Version _installedVersion;
    private readonly bool _supported;
    private GithubReleaseSnapshot? _selected;
    private UpdateManager? _manager;
    private UpdateInfo? _candidate;
    private string? _downloaded;
    public bool IsSupported => _supported;
    public string UnsupportedReason { get; }

    public VelopackAppUpdater(string repositoryUrl) : this(repositoryUrl, UpdateTransport.Client,
        source => new UpdateManager(source, new UpdateOptions { AllowVersionDowngrade = false, ExplicitChannel = "win-x64" })) { }

    internal VelopackAppUpdater(string repositoryUrl, HttpClient client,
        Func<IUpdateSource, UpdateManager> createManager, Version? installedVersion = null)
    {
        _installedVersion = installedVersion ?? AppVersion.Number;
        if (!GithubLatestReleaseClient.IsRepositoryUrl(repositoryUrl))
        {
            UnsupportedReason = "Esta compilação de desenvolvimento não tem um repositório de atualizações configurado.";
            return;
        }
        _catalog = new GithubLatestReleaseClient(repositoryUrl, client);
        _createManager = createManager;
        _supported = createManager(new EmptyUpdateSource()).IsInstalled;
        UnsupportedReason = "Para receber atualizações, use a versão instalada pelo instalador oficial.";
    }

    public async Task<AppRelease?> CheckAsync(CancellationToken token)
    {
        if (!IsSupported) throw new InvalidOperationException(UnsupportedReason);
        _selected = null; _manager = null; _candidate = null; _downloaded = null;
        var release = await _catalog!.GetLatestAsync(token);
        token.ThrowIfCancellationRequested();
        if (release == null || Version.Parse(release.Version) <= _installedVersion) return null;
        release.ValidateRequiredAssets();
        _selected = release;
        return new AppRelease(release.Version, release.Notes);
    }

    public async Task DownloadAsync(AppRelease release, IProgress<int> progress, CancellationToken token)
    {
        var selected = RequireSelection(release);
        _downloaded = null;
        _candidate = null;
        _manager = _createManager!(_catalog!.CreateSource(selected, token));
        var candidate = await _manager.CheckForUpdatesAsync()
            ?? throw new InvalidOperationException("O pacote da versão selecionada não está disponível.");
        token.ThrowIfCancellationRequested();
        if (candidate.IsDowngrade || candidate.TargetFullRelease.Version.ToString() != selected.Version)
            throw new InvalidOperationException("A versão do pacote difere da atualização selecionada.");
        _candidate = candidate;
        await _manager!.DownloadUpdatesAsync(candidate, value => progress.Report(value), token);
        token.ThrowIfCancellationRequested();
        _downloaded = release.Version;
    }

    public void ApplyAndRestart(AppRelease release)
    {
        RequireSelection(release);
        var candidate = _candidate;
        if (candidate == null || _manager == null || _downloaded != release.Version
            || candidate.TargetFullRelease.Version.ToString() != release.Version)
            throw new InvalidOperationException("Baixe e valide a atualização antes de instalar.");
        _manager!.WaitExitThenApplyUpdates(candidate.TargetFullRelease, silent: true, restart: true,
            restartArgs: Environment.GetCommandLineArgs().Skip(1).ToArray());
        System.Windows.Application.Current.Shutdown();
    }

    private GithubReleaseSnapshot RequireSelection(AppRelease release)
    {
        if (!IsSupported || _selected == null || _selected.Version != release.Version
            || !Version.TryParse(release.Version, out var version) || version <= _installedVersion)
            throw new InvalidOperationException("Verifique novamente as atualizações antes de continuar.");
        return _selected;
    }
}
