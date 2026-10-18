using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrilhaEstudos.App;
using Velopack;
using Velopack.Locators;

internal static class LatestReleaseChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var passed = 0;
        async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("OK   Consulta latest: " + name); }
        static void Expect(bool condition, string message) { if (!condition) throw new Exception(message); }
        static async Task Reject(Func<Task> action)
        {
            try { await action(); } catch (Exception) { return; }
            throw new Exception("A operação deveria ter falhado.");
        }
        await Test("versão atual ou inferior usa exatamente uma requisição", async () =>
        {
            foreach (var version in new[] { "1.0.1", "1.0.0" })
            {
                using var f = new Fixture(root) { ApiVersion = version };
                Expect(await f.Updater.CheckAsync(default) == null, "Foi oferecida versão igual/inferior.");
                Expect(f.Requests.Count == 1 && f.Requests[0] == Fixture.LatestUrl, "Consultou histórico ou manifesto.");
            }
        });
        await Test("nova versão e notas sem baixar manifesto ou pacote", async () =>
        {
            using var f = new Fixture(root);
            var release = await f.Updater.CheckAsync(default);
            Expect(release == new AppRelease("1.0.2", "Notas da release selecionada"), "Metadados incorretos.");
            Expect(f.Requests.Count == 1, "O download foi antecipado.");
            Expect(f.Identified && !f.HasAuthorization, "Identificação ausente ou credencial enviada.");
            await Reject(() => { f.Updater.ApplyAndRestart(release!); return Task.CompletedTask; });
        });
        await Test("ausência de release e erros HTTP", async () =>
        {
            using var missing = new Fixture(root) { ApiStatus = HttpStatusCode.NotFound };
            Expect(await missing.Updater.CheckAsync(default) == null && missing.Requests.Count == 1, "404 não tratado.");
            foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
            {
                using var f = new Fixture(root) { ApiStatus = status };
                await Reject(async () => { await f.Updater.CheckAsync(default); });
                Expect(f.Requests.Count == 1, "Erro HTTP disparou busca alternativa.");
            }
        });
        await Test("resposta inválida, rascunho e pré-release recusados", async () =>
        {
            foreach (var json in new[] { "{}", "inválido", "{\"tag_name\":\"v1.0.2-beta\",\"draft\":false,\"prerelease\":false}",
                "{\"tag_name\":\"v1.0.2\",\"draft\":true,\"prerelease\":false}",
                "{\"tag_name\":\"v1.0.2\",\"draft\":false,\"prerelease\":true}" })
            {
                using var f = new Fixture(root) { ApiOverride = json };
                await Reject(async () => { await f.Updater.CheckAsync(default); });
                Expect(f.Requests.Count == 1, "Resposta inválida disparou outros downloads.");
            }
        });
        await Test("falha de rede não consulta outros endpoints", async () =>
        {
            using var f = new Fixture(root) { NetworkFailure = true };
            await Reject(async () => { await f.Updater.CheckAsync(default); });
            Expect(f.Requests.Count == 1, "Falha disparou consulta ao histórico.");
        });
        await Test("cancelamento interrompe a requisição de metadados", async () =>
        {
            using var f = new Fixture(root) { BlockApi = true };
            using var token = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try { await f.Updater.CheckAsync(token.Token); throw new Exception("Consulta não cancelada."); }
            catch (OperationCanceledException) { }
            Expect(f.CancelObserved && f.Requests.Count == 1, "Requisição continuou após cancelar.");
        });
        await Test("download usa apenas a release selecionada após outra publicação", async () =>
        {
            using var f = new Fixture(root);
            var release = (await f.Updater.CheckAsync(default))!;
            f.ApiVersion = "1.0.3";
            await f.Updater.DownloadAsync(release, new Progress<int>(), default);
            Expect(f.Requests.SequenceEqual(new[] { Fixture.LatestUrl, Fixture.Base + Fixture.Manifest, Fixture.Base + Fixture.Package }), "Consultou outra release ou histórico.");
            var file = Path.Combine(f.PackagesDirectory, Fixture.Package);
            Expect(File.Exists(file) && File.ReadAllBytes(file).SequenceEqual(f.PackageBytes), "Pacote selecionado não foi validado e salvo.");
        });
        await Test("manifesto divergente bloqueia pacote e instalação", async () =>
        {
            foreach (var change in new[] { "versão", "aplicativo", "canal", "hash", "json" })
            {
                using var f = new Fixture(root);
                f.ManifestOverride = change switch
                {
                    "versão" => f.Feed().Replace("1.0.2", "1.0.3"),
                    "aplicativo" => f.Feed().Replace("TrilhaEstudos.App", "OutroAplicativo"),
                    "canal" => f.Feed().Replace("win-x64", "win-arm64"),
                    "hash" => f.Feed().Replace(Convert.ToHexString(SHA256.HashData(f.PackageBytes)), ""),
                    _ => "inválido"
                };
                var release = (await f.Updater.CheckAsync(default))!;
                await Reject(() => f.Updater.DownloadAsync(release, new Progress<int>(), default));
                await Reject(() => { f.Updater.ApplyAndRestart(release); return Task.CompletedTask; });
                Expect(f.Requests.Count == 2, "Pacote divergente chegou a ser baixado.");
            }
        });
        await Test("endereços fora da release são recusados", async () =>
        {
            using var f = new Fixture(root) { WrongAssetUrl = true };
            await Reject(async () => { await f.Updater.CheckAsync(default); });
            Expect(f.Requests.Count == 1, "Acessou endereço não pertencente à release.");
        });
        await Test("cancelamento interrompe a leitura do manifesto", async () =>
        {
            using var f = new Fixture(root) { BlockManifest = true };
            var release = (await f.Updater.CheckAsync(default))!;
            using var token = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try { await f.Updater.DownloadAsync(release, new Progress<int>(), token.Token); throw new Exception("Manifesto não cancelado."); }
            catch (OperationCanceledException) { }
            Expect(f.CancelObserved && f.Requests.Count == 2, "Download do manifesto continuou após cancelar.");
            await Reject(() => { f.Updater.ApplyAndRestart(release); return Task.CompletedTask; });
        });
        await Test("pacote corrompido não habilita instalação", async () =>
        {
            using var f = new Fixture(root) { CorruptPackage = true };
            var release = (await f.Updater.CheckAsync(default))!;
            await Reject(() => f.Updater.DownloadAsync(release, new Progress<int>(), default));
            await Reject(() => { f.Updater.ApplyAndRestart(release); return Task.CompletedTask; });
            Expect(f.Requests.Skip(1).All(url => url.StartsWith(Fixture.Base, StringComparison.Ordinal)), "Recuou para outra release.");
        });
        await Test("cancelamento interrompe o corpo do pacote", async () =>
        {
            using var f = new Fixture(root) { BlockPackage = true };
            var release = (await f.Updater.CheckAsync(default))!;
            using var token = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try { await f.Updater.DownloadAsync(release, new Progress<int>(), token.Token); throw new Exception("Pacote não cancelado."); }
            catch (OperationCanceledException) { }
            Expect(f.CancelObserved, "Leitura do pacote continuou após cancelar.");
            await Reject(() => { f.Updater.ApplyAndRestart(release); return Task.CompletedTask; });
        });
        return passed;
    }

    private sealed class Fixture : HttpMessageHandler
    {
        internal const string Repository = "https://github.com/tiagoguerreirodev/trilha-estudos";
        internal const string LatestUrl = "https://api.github.com/repos/tiagoguerreirodev/trilha-estudos/releases/latest";
        internal const string Base = Repository + "/releases/download/v1.0.2/";
        internal const string Manifest = "releases.win-x64.json";
        internal const string Package = "TrilhaEstudos.App-1.0.2-win-x64-full.nupkg";
        private readonly HttpClient _client;
        internal List<string> Requests { get; } = [];
        internal string ApiVersion { get; set; } = "1.0.2";
        internal HttpStatusCode ApiStatus { get; init; } = HttpStatusCode.OK;
        internal string? ApiOverride { get; init; }
        internal string? ManifestOverride { get; set; }
        internal bool WrongAssetUrl { get; init; }
        internal bool BlockApi { get; init; }
        internal bool BlockManifest { get; init; }
        internal bool BlockPackage { get; init; }
        internal bool NetworkFailure { get; init; }
        internal bool CorruptPackage { get; init; }
        internal bool CancelObserved { get; private set; }
        internal bool Identified { get; private set; } = true;
        internal bool HasAuthorization { get; private set; }
        internal byte[] PackageBytes { get; }
        internal string PackagesDirectory { get; }
        internal VelopackAppUpdater Updater { get; }

        internal Fixture(string root)
        {
            PackagesDirectory = Path.Combine(root, "http-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(PackagesDirectory);
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var writer = new StreamWriter(zip.CreateEntry("TrilhaEstudos.App.nuspec").Open(), Encoding.UTF8);
                writer.Write("<package><metadata><id>TrilhaEstudos.App</id><version>1.0.2</version><authors>Trilha Estudos</authors><description>Pacote de verificação</description></metadata></package>");
            }
            PackageBytes = memory.ToArray();
            _client = new HttpClient(this, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            Updater = new VelopackAppUpdater(Repository, _client, source => new UpdateManager(source,
                new UpdateOptions { ExplicitChannel = "win-x64", AllowVersionDowngrade = false },
                new TestVelopackLocator("TrilhaEstudos.App", "1.0.1", PackagesDirectory, null!)), new Version("1.0.1"));
        }

        internal string Feed() => JsonSerializer.Serialize(new { Assets = new[] { new
        {
            PackageId = "TrilhaEstudos.App", Version = "1.0.2", Type = "Full", FileName = Package,
            SHA1 = Convert.ToHexString(SHA1.HashData(PackageBytes)), SHA256 = Convert.ToHexString(SHA256.HashData(PackageBytes)),
            Size = PackageBytes.Length
        } } });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!.AbsoluteUri; Requests.Add(url);
            Identified &= request.Headers.UserAgent.ToString().Contains("TrilhaEstudos/", StringComparison.Ordinal);
            HasAuthorization |= request.Headers.Authorization != null;
            if (NetworkFailure) throw new HttpRequestException("Falha simulada de rede.");
            if ((url == LatestUrl && BlockApi) || (url == Base + Manifest && BlockManifest))
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { CancelObserved = token.IsCancellationRequested; }
            }
            if (url == LatestUrl)
            {
                var downloadBase = Repository + "/releases/download/v" + ApiVersion + "/";
                var json = ApiOverride ?? JsonSerializer.Serialize(new
                {
                    tag_name = "v" + ApiVersion, draft = false, prerelease = false, body = "Notas da release selecionada",
                    assets = new[] {
                        new { name = Manifest, browser_download_url = WrongAssetUrl ? "https://example.org/manifest.json" : downloadBase + Manifest },
                        new { name = $"TrilhaEstudos.App-{ApiVersion}-win-x64-full.nupkg", browser_download_url = downloadBase + $"TrilhaEstudos.App-{ApiVersion}-win-x64-full.nupkg" }
                    }
                });
                return new HttpResponseMessage(ApiStatus) { Content = new StringContent(json) };
            }
            if (url == Base + Manifest) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ManifestOverride ?? Feed()) };
            if (url == Base + Package)
            {
                if (BlockPackage) return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StreamContent(new CancelledReadStream(() => CancelObserved = true)) };
                var bytes = PackageBytes.ToArray(); if (CorruptPackage) bytes[0] ^= 255;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }
            throw new Exception("Requisição inesperada: " + url);
        }

        protected override void Dispose(bool disposing) { if (disposing) _client.Dispose(); base.Dispose(disposing); }
    }

    private sealed class CancelledReadStream(Action cancelled) : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            try { await Task.Delay(Timeout.Infinite, token); return 0; }
            finally { if (token.IsCancellationRequested) cancelled(); }
        }
    }
}
