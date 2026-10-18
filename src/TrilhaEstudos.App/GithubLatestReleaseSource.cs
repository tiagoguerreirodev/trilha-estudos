using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace TrilhaEstudos.App;

internal static class UpdateTransport
{
    // Mantém conexões e as configurações de proxy do Windows; não envia credenciais.
    internal static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal static HttpRequestMessage Request(Uri uri, bool api = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd($"TrilhaEstudos/{AppVersion.Semantic}");
        if (api) request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }
}

internal sealed record GithubReleaseSnapshot(string Version, string Notes, string DownloadBase,
    IReadOnlyDictionary<string, string> Assets)
{
    internal const string PackageId = "TrilhaEstudos.App";
    internal const string Channel = "win-x64";
    internal const string ManifestName = "releases.win-x64.json";
    internal string PackageName => $"{PackageId}-{Version}-{Channel}-full.nupkg";

    internal Uri AssetUri(string name)
    {
        var expected = DownloadBase + Uri.EscapeDataString(name);
        if (!Assets.TryGetValue(name, out var url) || !string.Equals(url, expected, StringComparison.Ordinal)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidDataException("Os arquivos da release selecionada estão ausentes ou têm um endereço inválido.");
        return uri;
    }

    internal void ValidateRequiredAssets() { AssetUri(ManifestName); AssetUri(PackageName); }
}

internal sealed class GithubLatestReleaseClient(string repositoryUrl, HttpClient client)
{
    internal static bool IsRepositoryUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "github.com" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && Regex.IsMatch(uri.AbsolutePath.TrimEnd('/'), @"^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");

    internal async Task<GithubReleaseSnapshot?> GetLatestAsync(CancellationToken token)
    {
        var path = new Uri(repositoryUrl).AbsolutePath.TrimEnd('/');
        using var request = UpdateTransport.Request(new Uri($"https://api.github.com/repos{path}/releases/latest"), api: true);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var semantic = tag.StartsWith('v') ? tag[1..] : tag;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()
            || !Regex.IsMatch(semantic, @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\z")
            || !Version.TryParse(semantic, out _))
            throw new InvalidDataException("O GitHub retornou uma versão estável inválida.");
        var assets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name == null || url == null || !assets.TryAdd(name, url))
                throw new InvalidDataException("A lista de arquivos da release é inválida.");
        }
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new GithubReleaseSnapshot(semantic, notes, $"https://github.com{path}/releases/download/{Uri.EscapeDataString(tag)}/", assets);
    }

    internal IUpdateSource CreateSource(GithubReleaseSnapshot release, CancellationToken token)
        => new SelectedGithubReleaseSource(client, release, token);
}

internal sealed class EmptyUpdateSource : IUpdateSource
{
    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
        Guid? stagingId = null, VelopackAsset? latestLocalRelease = null) => Task.FromResult(new VelopackAssetFeed());
    public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
        Action<int> progress, CancellationToken cancelToken = default) => throw new InvalidOperationException("Nenhuma versão selecionada.");
}

internal sealed class SelectedGithubReleaseSource(HttpClient client, GithubReleaseSnapshot release,
    CancellationToken manifestToken) : IUpdateSource
{
    public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
        Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        if (channel != GithubReleaseSnapshot.Channel || (appId != null && appId != GithubReleaseSnapshot.PackageId))
            throw new InvalidDataException("O pacote não corresponde a este aplicativo ou canal.");
        using var request = UpdateTransport.Request(release.AssetUri(GithubReleaseSnapshot.ManifestName));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, manifestToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(manifestToken).ConfigureAwait(false);
        var feed = VelopackAssetFeed.FromJson(json);
        var full = feed.Assets.Where(a => a.Type == VelopackAssetType.Full).ToArray();
        if (full.Length != 1 || full[0].PackageId != GithubReleaseSnapshot.PackageId
            || full[0].Version.ToString() != release.Version || full[0].FileName != release.PackageName
            || full[0].Size <= 0 || !Regex.IsMatch(full[0].SHA256 ?? "", @"\A[0-9a-fA-F]{64}\z"))
            throw new InvalidDataException("O manifesto não corresponde à versão selecionada ou está incompleto.");
        // O MVP publica pacotes completos; arquivos de outras versões/canais nunca entram na seleção.
        return new VelopackAssetFeed { Assets = full };
    }

    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
        Action<int> progress, CancellationToken cancelToken = default)
    {
        if (releaseEntry.FileName != release.PackageName || releaseEntry.Version.ToString() != release.Version
            || releaseEntry.PackageId != GithubReleaseSnapshot.PackageId || releaseEntry.Type != VelopackAssetType.Full)
            throw new InvalidDataException("O pacote não corresponde à atualização selecionada.");
        using var request = UpdateTransport.Request(release.AssetUri(releaseEntry.FileName));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancelToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
        await using var output = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long total = 0; var reported = -1;
        int count;
        while ((count = await input.ReadAsync(buffer, cancelToken).ConfigureAwait(false)) != 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count), cancelToken).ConfigureAwait(false);
            total += count;
            var percent = (int)Math.Clamp(total * 100d / releaseEntry.Size, 0, 100);
            if (percent != reported) { progress(percent); reported = percent; }
        }
        // A verificação de tamanho e hash permanece a cargo do UpdateManager do Velopack.
        cancelToken.ThrowIfCancellationRequested();
    }
}
