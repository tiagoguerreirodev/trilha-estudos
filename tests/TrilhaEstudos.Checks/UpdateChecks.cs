using System.IO;
using TrilhaEstudos.App;
using TrilhaEstudos.Core;

internal static class UpdateChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var passed = 0;
        var newer = new Version(AppVersion.Number.Major, AppVersion.Number.Minor, AppVersion.Number.Build + 1).ToString(3);
        async Task Test(string name, Func<Task> action)
        {
            await action(); passed++; Console.WriteLine("OK   Atualizações: " + name);
        }
        void Expect(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task<StudyStore> Store()
        {
            var store = new StudyStore(Path.Combine(root, "updates-" + Guid.NewGuid().ToString("N"), "data.db"));
            await store.InitializeAsync();
            var p = await store.SavePlanAsync(null, "Estudos preservados", "", null, "nota");
            var d = await store.SaveDisciplineAsync(null, p, "Disciplina");
            var t = await store.SaveTopicAsync(null, d, null, "Assunto", TopicStatus.InProgress, Priority.High, "observação");
            await store.SaveSessionAsync(null, t, SessionKind.Study, new DateOnly(2026, 1, 1), 40, "sessão");
            return store;
        }
        await Test("versão atual e versões semânticas", async () =>
        {
            Expect(AppVersion.Semantic == AppVersion.Number.ToString(3), "Versão semântica divergente.");
            if (AppVersion.Semantic == "1.0.0") Expect(AppVersion.Display == "1.0", "Versão inicial divergente.");
            Expect(new Version("1.0.10") > new Version("1.0.9"), "Comparação de versões deve ser numérica.");
            var store = await Store();
            foreach (var version in new[] { AppVersion.Semantic, "0.0.0", newer + "-beta", "inválida" })
            {
                var vm = new UpdateViewModel(new FakeUpdater { Release = new(version, "") }, store);
                await vm.CheckAsync(false);
                Expect(vm.State == AppUpdateState.Current && !vm.HasUpdate, "Versão igual, inferior ou inválida foi oferecida.");
            }
        });
        await Test("nova versão, notas e ausência de release", async () =>
        {
            var store = await Store();
            var vm = new UpdateViewModel(new FakeUpdater { Release = new(newer, "Correções") }, store);
            await vm.CheckAsync(false);
            Expect(vm.State == AppUpdateState.Available && vm.ReleaseNotes == "Correções", "Nova versão não apresentada.");
            var empty = new UpdateViewModel(new FakeUpdater(), store);
            await empty.CheckAsync(false);
            Expect(empty.State == AppUpdateState.Current, "Ausência de release não tratada.");
        });
        await Test("falha de rede preserva os estudos", async () =>
        {
            var store = await Store();
            var vm = new UpdateViewModel(new FakeUpdater { CheckFailure = new IOException("offline") }, store);
            await vm.CheckOnStartupAsync();
            Expect(vm.State == AppUpdateState.Idle && vm.Status.Contains("continuam"), "Consulta automática bloqueou o uso.");
            await vm.CheckAsync(false);
            Expect(vm.Status.Contains("conexão"), "Erro manual incompreensível.");
            Expect((await store.ReadAsync()).Sessions.Count == 1, "Falha de rede afetou os estudos.");
        });
        await Test("cancelamento não habilita a instalação", async () =>
        {
            var store = await Store();
            var fake = new FakeUpdater { Release = new(newer, ""), CancelDownload = true };
            var vm = new UpdateViewModel(fake, store, confirm: () => true);
            await vm.CheckAsync(false); await vm.DownloadAsync(); await vm.ApplyAsync();
            Expect(vm.State == AppUpdateState.Available && fake.ApplyCount == 0, "Download cancelado permitiu instalar.");
        });
        await Test("pacote inválido não habilita a instalação", async () =>
        {
            var store = await Store();
            var fake = new FakeUpdater { Release = new(newer, ""), DownloadFailure = new InvalidDataException("checksum") };
            var vm = new UpdateViewModel(fake, store, confirm: () => true);
            await vm.CheckAsync(false); await vm.DownloadAsync(); await vm.ApplyAsync();
            Expect(vm.State == AppUpdateState.Available && fake.ApplyCount == 0 && vm.Status.Contains("validar"), "Falha de integridade foi ignorada.");
        });
        await Test("confirmação e backup consistente antes de aplicar", async () =>
        {
            var store = await Store();
            await store.BackupAsync();
            var fake = new FakeUpdater { Release = new(newer, "") };
            var confirmed = false;
            var vm = new UpdateViewModel(fake, store, confirm: () => confirmed);
            await vm.CheckAsync(false); await vm.DownloadAsync(); await vm.ApplyAsync();
            Expect(fake.ApplyCount == 0 && vm.State == AppUpdateState.Ready, "Instalou sem confirmação.");
            confirmed = true; await vm.ApplyAsync();
            Expect(fake.ApplyCount == 1 && File.Exists(vm.BackupPath), "Não foi criado backup antes da instalação. " + vm.Status);
            var backup = await new StudyStore(vm.BackupPath).ReadAsync();
            Expect(backup.Sessions.Single().Minutes == 40 && backup.Cycles.Count == 1 && backup.Plans.Single().Name == "Estudos preservados", "Backup incompleto.");
        });
        await Test("falha no backup bloqueia a aplicação", async () =>
        {
            var store = await Store();
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(store.DatabasePath)!, "backups"), "Bloqueio de teste");
            var fake = new FakeUpdater { Release = new(newer, "") };
            var vm = new UpdateViewModel(fake, store, confirm: () => true);
            await vm.CheckAsync(false); await vm.DownloadAsync(); await vm.ApplyAsync();
            Expect(fake.ApplyCount == 0 && vm.State == AppUpdateState.Ready && vm.Status.Contains("continua aberto"), "Instalou apesar da falha no backup.");
        });
        await Test("aguarda gravações e não consulta no desenvolvimento", async () =>
        {
            var store = await Store();
            var fake = new FakeUpdater { Release = new(newer, "") };
            var vm = new UpdateViewModel(fake, store, canApply: () => false, confirm: () => true);
            await vm.CheckAsync(false); await vm.DownloadAsync(); await vm.ApplyAsync();
            Expect(fake.ApplyCount == 0, "Aplicou enquanto uma operação estava ativa.");
            var development = new UpdateViewModel(new UnavailableAppUpdater(), store);
            await development.CheckOnStartupAsync();
            Expect(development.State == AppUpdateState.Unavailable, "Compilação de desenvolvimento iniciou consulta.");
        });
        return passed;
    }
    private sealed class FakeUpdater : IAppUpdater
    {
        public bool IsSupported => true;
        public string UnsupportedReason => "";
        public AppRelease? Release { get; init; }
        public Exception? CheckFailure { get; init; }
        public Exception? DownloadFailure { get; init; }
        public bool CancelDownload { get; init; }
        public int ApplyCount { get; private set; }
        public Task<AppRelease?> CheckAsync(CancellationToken token) => CheckFailure != null
            ? Task.FromException<AppRelease?>(CheckFailure) : Task.FromResult(Release);
        public Task DownloadAsync(AppRelease release, IProgress<int> progress, CancellationToken token)
        {
            if (CancelDownload) throw new OperationCanceledException();
            if (DownloadFailure != null) throw DownloadFailure;
            progress.Report(100); return Task.CompletedTask;
        }
        public void ApplyAndRestart(AppRelease release) => ApplyCount++;
    }
}
