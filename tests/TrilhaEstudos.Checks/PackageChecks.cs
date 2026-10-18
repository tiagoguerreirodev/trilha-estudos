using System.IO;
using TrilhaEstudos.App;
using TrilhaEstudos.Core;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

internal static class PackageChecks
{
    public static async Task RunAsync(string feed, string install, string data)
    {
        var packages = Path.Combine(install, "packages");
        var locator = new TestVelopackLocator("TrilhaEstudos.UpdateTest", "1.0.0", packages,
            Path.Combine(install, "current"), install, Path.Combine(install, "Update.exe"),
            "win-x64", null!, null!, Path.Combine(install, "current", "TrilhaEstudos.exe"));
        var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(feed)),
            new UpdateOptions { ExplicitChannel = "win-x64" }, locator);
        var updater = new VelopackAppUpdater(manager);
        var release = await updater.CheckAsync(CancellationToken.None)
            ?? throw new Exception("Pacote 1.0.1 não encontrado.");
        if (release.Version != "1.0.1") throw new Exception("Versão incorreta no feed.");
        await updater.DownloadAsync(release, new Progress<int>(), CancellationToken.None);
        Console.WriteLine("OK   Pacote real: consulta e download com integridade validada");
        foreach (var installedVersion in new[] { "1.0.1", "1.0.2" })
        {
            var currentLocator = new TestVelopackLocator("TrilhaEstudos.UpdateTest", installedVersion,
                Path.Combine(data, "current-" + installedVersion), null!);
            var currentManager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(feed)),
                new UpdateOptions { ExplicitChannel = "win-x64" }, currentLocator);
            if (await currentManager.CheckForUpdatesAsync() != null) throw new Exception("Feed real ofereceu versão igual ou inferior.");
        }
        Console.WriteLine("OK   Pacote real: versões iguais e inferiores não oferecidas");

        var broken = Path.Combine(data, "corrupt-feed"); Directory.CreateDirectory(broken);
        foreach (var file in Directory.GetFiles(feed)) File.Copy(file, Path.Combine(broken, Path.GetFileName(file)), true);
        var package = Directory.GetFiles(broken, "*-full.nupkg").Single();
        using (var file = File.Open(package, FileMode.Open, FileAccess.ReadWrite))
        { file.Position = 50; var original = file.ReadByte(); file.Position = 50; file.WriteByte((byte)(original ^ 255)); }
        var badLocator = new TestVelopackLocator("TrilhaEstudos.UpdateTest", "1.0.0", Path.Combine(data, "bad-downloads"), null!);
        var badManager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(broken)),
            new UpdateOptions { ExplicitChannel = "win-x64" }, badLocator);
        var badUpdater = new VelopackAppUpdater(badManager);
        var badRelease = await badUpdater.CheckAsync(CancellationToken.None) ?? throw new Exception("Feed corrompido ausente.");
        var rejected = false;
        try { await badUpdater.DownloadAsync(badRelease, new Progress<int>(), CancellationToken.None); }
        catch (Exception) { rejected = true; }
        if (!rejected) throw new Exception("Pacote real corrompido foi aceito.");
        Console.WriteLine("OK   Pacote real: conteúdo corrompido rejeitado");

        var store = new StudyStore(Path.Combine(data, "estudos.db")); await store.InitializeAsync();
        var plan = await store.SavePlanAsync(null, "Persistência na atualização", "", null, "preservar");
        var discipline = await store.SaveDisciplineAsync(null, plan, "Disciplina de teste");
        var topic = await store.SaveTopicAsync(null, discipline, null, "Assunto de teste", TopicStatus.InProgress, Priority.High, "nota");
        await store.SaveSessionAsync(null, topic, SessionKind.Study, new DateOnly(2026, 1, 1), 40, "anotação");
        await store.SaveIntervalsAsync("2,8");
        await store.BackupAsync();
        // O processo auxiliar aguarda esta verificação encerrar normalmente antes de substituir os binários.
        manager.WaitExitThenApplyUpdates(null, restart: false, silent: true);
        Console.WriteLine("OK   Atualização real preparada; o auxiliar aguarda a saída deste processo");
    }

    public static async Task VerifyAsync(string data)
    {
        var snapshot = await new StudyStore(Path.Combine(data, "estudos.db")).ReadAsync();
        if (snapshot.Plans.Single().Name != "Persistência na atualização" || snapshot.Sessions.Single().Minutes != 40
            || snapshot.Cycles.Count != 1 || !snapshot.Intervals.SequenceEqual(new[] { 2, 8 }))
            throw new Exception("Dados alterados pela atualização.");
        Console.WriteLine("OK   Atualização 1.0.0 → 1.0.1 preservou edital, sessão, revisão e configurações");
    }
}
