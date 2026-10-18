using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrilhaEstudos.Core;

internal static class PreviousStudyChecks
{
    internal static async Task<int> RunAsync(string root)
    {
        var passed = 0;
        async Task Check(string name, Func<StudyStore, Task> test)
        {
            var store = new StudyStore(Path.Combine(root, "previous-" + Guid.NewGuid().ToString("N"), "data.db"));
            await store.InitializeAsync(); await test(store); passed++;
            Console.WriteLine("OK   " + name);
        }
        await Check("Estudo anterior conclui sem sessão, tempo ou revisão; repetição e remoção", async store =>
        {
            var (_, _, t) = await Seed(store);
            var before = await store.ReadAsync();
            await store.SetPreviouslyStudiedAsync(t, true);
            var s = await store.ReadAsync();
            Require(s.Topics.Single().PreviouslyStudied && s.Summary().Percent == 100, "Marcação não concluiu.");
            Require(s.Sessions.Count == 0 && s.Cycles.Count == 0 && s.Summary().Minutes == 0, "Criou tempo ou histórico de sessão.");
            Require(s.Activities.Count == before.Activities.Count + 1, "Atividade ausente.");
            await store.SetPreviouslyStudiedAsync(t, true);
            Require((await store.ReadAsync()).Activities.Count == s.Activities.Count, "Atividade duplicada.");
            var reopened = new StudyStore(store.DatabasePath); await reopened.InitializeAsync();
            Require((await reopened.ReadAsync()).Topics.Single().PreviouslyStudied, "Marcação não persistiu.");
            await reopened.SetPreviouslyStudiedAsync(t, false);
            s = await reopened.ReadAsync();
            Require(!s.Topics.Single().PreviouslyStudied && s.Topics.Single().Status == TopicStatus.Completed, "Remoção alterou status.");
            await reopened.SetPreviouslyStudiedAsync(t, false);
            Require((await reopened.ReadAsync()).Activities.Count == s.Activities.Count, "Remoção repetida gerou atividade.");
        });
        await Check("Edições preservam estudo anterior e ciclos/sessões existentes", async store =>
        {
            var (_, d, t) = await Seed(store);
            await store.SaveSessionAsync(null, t, SessionKind.Study, DateOnly.FromDateTime(DateTime.Today).AddDays(-10), 55, "estudo");
            await store.SaveSessionAsync(null, t, SessionKind.Review, DateOnly.FromDateTime(DateTime.Today).AddDays(-8), 15, "revisão");
            var before = await store.ReadAsync();
            await store.SetPreviouslyStudiedAsync(t, true);
            await store.SaveTopicAsync(t, d, null, "Nome editado", TopicStatus.InProgress, Priority.High, "Notas editadas");
            var after = await store.ReadAsync();
            Require(after.Topics.Single().PreviouslyStudied && after.Topics.Single().Status == TopicStatus.InProgress, "Editar perdeu marcação ou status manual.");
            Require(JsonSerializer.Serialize(after.Sessions) == JsonSerializer.Serialize(before.Sessions)
                && JsonSerializer.Serialize(after.Cycles) == JsonSerializer.Serialize(before.Cycles) && after.Summary().Minutes == 70, "Sessões, ciclos ou tempo alterados.");
        });
        await Check("Grupo total/parcial, filhos novos e remoção conservam estados", async store =>
        {
            var (_, d, parent) = await Seed(store);
            var a = await store.SaveTopicAsync(null, d, parent, "Filho A", TopicStatus.NotStarted, Priority.Low, "A");
            var b = await store.SaveTopicAsync(null, d, parent, "Filho B", TopicStatus.InProgress, Priority.High, "B");
            await store.SetPreviouslyStudiedAsync(a, true);
            var s = await store.ReadAsync();
            Require(s.PreviousStudyLabel(s.Topics.Single(t => t.Id == parent)).Contains("1 de 2"), "Grupo parcial incorreto.");
            await store.SetPreviouslyStudiedAsync(parent, true);
            s = await store.ReadAsync();
            Require(s.Topics.All(t => t.PreviouslyStudied && t.Status == TopicStatus.Completed) && s.Summary().Completed == 2, "Grupo não concluiu folhas.");
            Require(s.PreviousStudyLabel(s.Topics.Single(t => t.Id == parent)).Contains("Todos"), "Grupo total incorreto.");
            var child = await store.SaveTopicAsync(null, d, parent, "Novo", TopicStatus.NotStarted, Priority.Medium, "");
            s = await store.ReadAsync();
            Require(!s.Topics.Single(t => t.Id == child).PreviouslyStudied && s.PreviousStudyLabel(s.Topics.Single(t => t.Id == parent)).Contains("2 de 3"), "Novo filho herdou marcação.");
            await store.SetPreviouslyStudiedAsync(parent, false);
            s = await store.ReadAsync();
            Require(s.Topics.All(t => !t.PreviouslyStudied) && s.Summary().Completed == 2 && s.Summary().NotStarted == 1, "Remoção alterou status dos filhos.");
            Require(s.PreviousStudyLabel(s.Topics.Single(t => t.Id == parent)).Contains("nenhum"), "Grupo sem marcação incorreto.");
            Require(s.Topics.Single(t => t.Id == a).Priority == Priority.Low && s.Topics.Single(t => t.Id == b).Notes == "B", "Metadados alterados.");
        });
        await Check("Marcação em outro grupo/edital é isolada; assunto inválido é rejeitado", async store =>
        {
            var (_, _, t) = await Seed(store);
            var (_, _, other) = await Seed(store);
            await store.SetPreviouslyStudiedAsync(t, true);
            Require(!(await store.ReadAsync()).Topics.Single(x => x.Id == other).PreviouslyStudied, "Alterou outro edital.");
            await Reject(() => store.SetPreviouslyStudiedAsync(int.MaxValue, true), "válido");
        });
        await Check("Falha ao registrar atividade reverte marcação e conclusão em grupo", async store =>
        {
            var (_, d, t) = await Seed(store);
            await store.SaveTopicAsync(null, d, t, "Filho", TopicStatus.NotStarted, Priority.Low, "nota");
            var before = JsonSerializer.Serialize(await store.ReadAsync());
            Sql(store.DatabasePath, "CREATE TRIGGER FailPreviousStudy BEFORE INSERT ON Activity BEGIN SELECT RAISE(ABORT,'forced'); END;");
            await Reject(() => store.SetPreviouslyStudiedAsync(t, true), "não foram reinicializados");
            Require(JsonSerializer.Serialize(await store.ReadAsync()) == before, "Gravação parcial após falha.");
        });
        await Check("Migração 1→2 preserva todos os registros e valida backup anterior", async store =>
        {
            var (_, _, t) = await Seed(store);
            var today = DateOnly.FromDateTime(DateTime.Today);
            await store.SaveSessionAsync(null, t, SessionKind.Study, today.AddDays(-10), 50, "estudo antigo");
            await store.SaveSessionAsync(null, t, SessionKind.Review, today.AddDays(-5), 12, "revisão antiga");
            await store.SaveIntervalsAsync("3,8");
            var before = JsonSerializer.Serialize(await store.ReadAsync());
            DowngradeFixture(store.DatabasePath);
            var original = Dump(store.DatabasePath);
            await new StudyStore(store.DatabasePath).InitializeAsync();
            Require(JsonSerializer.Serialize(await store.ReadAsync()) == before, "Migração alterou dados ou marcou estudos antigos.");
            Require(Scalar(store.DatabasePath, "PRAGMA user_version") == "2", "Esquema não avançou.");
            var folder = Path.Combine(Path.GetDirectoryName(store.DatabasePath)!, "backups");
            var backup = Directory.GetFiles(folder).Single();
            Require(Dump(backup) == original && Scalar(backup, "PRAGMA integrity_check") == "ok" && Scalar(backup, "PRAGMA user_version") == "1", "Backup anterior inválido.");
            await store.InitializeAsync();
            Require(Directory.GetFiles(folder).Length == 1, "Inicialização repetiu migração/backup.");
        });
        await Check("Falha de backup impede migração e preserva banco antigo", async store =>
        {
            await Seed(store); DowngradeFixture(store.DatabasePath);
            var original = Dump(store.DatabasePath);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(store.DatabasePath)!, "backups"), "bloquear pasta");
            await Reject(() => store.InitializeAsync(), "migração não foi iniciada");
            Require(Dump(store.DatabasePath) == original && Scalar(store.DatabasePath, "PRAGMA user_version") == "1", "Alterou banco após falha no backup.");
        });
        await Check("Falha de migração preserva esquema e banco; novo banco inicia no esquema 2", async store =>
        {
            Require(Scalar(store.DatabasePath, "PRAGMA user_version") == "2" && !Directory.Exists(Path.Combine(Path.GetDirectoryName(store.DatabasePath)!, "backups")), "Banco novo exige migração.");
            await Seed(store);
            // Simula banco inconsistente: versão antiga com coluna já presente. O ALTER deve falhar sem commit.
            Sql(store.DatabasePath, "PRAGMA user_version=1;"); var before = Dump(store.DatabasePath);
            await Reject(() => store.InitializeAsync(), "não foram reinicializados");
            Require(Dump(store.DatabasePath) == before && Scalar(store.DatabasePath, "PRAGMA user_version") == "1", "Migração falha foi parcialmente aplicada.");
        });
        return passed;
    }
    private static async Task<(int Plan, int Discipline, int Topic)> Seed(StudyStore store)
    {
        var p = await store.SavePlanAsync(null, "Plano de teste", "Instituição", null, "Observações");
        var d = await store.SaveDisciplineAsync(null, p, "Disciplina de teste");
        var t = await store.SaveTopicAsync(null, d, null, "Assunto de teste", TopicStatus.NotStarted, Priority.Medium, "Nota");
        return (p, d, t);
    }
    private static void DowngradeFixture(string path) => Sql(path, "ALTER TABLE Topic DROP COLUMN PreviouslyStudied; PRAGMA user_version=1;");
    private static SqliteConnection Open(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); c.Open(); return c;
    }
    private static void Sql(string path, string sql) { using var c = Open(path); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    private static string Scalar(string path, string sql) { using var c = Open(path); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar()!.ToString()!; }
    private static string Dump(string path)
    {
        using var c = Open(path); var tables = new Dictionary<string, List<object?[]>>();
        foreach (var table in new[] { "StudyPlan", "Discipline", "Topic", "StudySession", "ReviewCycle", "Setting", "Activity" })
        {
            using var cmd = c.CreateCommand(); cmd.CommandText = $"SELECT * FROM {table} ORDER BY 1"; using var reader = cmd.ExecuteReader();
            var rows = new List<object?[]>();
            while (reader.Read()) { var row = new object[reader.FieldCount]; reader.GetValues(row); rows.Add(row); }
            tables[table] = rows;
        }
        return JsonSerializer.Serialize(tables);
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); }
        catch (InvalidOperationException ex) { Require(ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase), ex.Message); return; }
        throw new Exception("Operação deveria ser rejeitada.");
    }
}
