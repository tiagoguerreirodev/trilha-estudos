using System.Text.Json;
using System.IO;
using Microsoft.Data.Sqlite;
using TrilhaEstudos.Core;

internal static class DisciplineChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var passed = 0;
        async Task Check(string name, Func<StudyStore, Task> test)
        {
            var store = new StudyStore(Path.Combine(root, Guid.NewGuid().ToString("N"), "discipline.db"));
            await store.InitializeAsync();
            await test(store); passed++; Console.WriteLine("OK   Disciplina: " + name);
        }
        await Check("marcação integral, remoção, persistência e preservação de estudos", async store =>
        {
            var (p, d, group, child) = await Seed(store);
            var today = DateOnly.FromDateTime(DateTime.Today);
            await store.SaveSessionAsync(null, group, SessionKind.Study, today.AddDays(-10), 40, "Estudo real");
            await store.SaveSessionAsync(null, group, SessionKind.Review, today.AddDays(-8), 15, "Revisão real");
            await store.SaveIntervalsAsync("2,9");
            var before = await store.ReadAsync();
            await store.SetDisciplinePreviouslyStudiedAsync(d, true);
            var after = await store.ReadAsync();
            Require(after.Topics.All(t => t.PreviouslyStudied && t.Status == TopicStatus.Completed), "Nem todos foram marcados/concluídos.");
            Require(after.Summary().Completed == 3 && after.Summary().Minutes == 55, "Progresso ou tempo incorretos.");
            Require(before.Topics.Select(t => (t.Id,t.Name,t.Priority,t.Notes,t.Position,t.ParentId)).SequenceEqual(
                after.Topics.Select(t => (t.Id,t.Name,t.Priority,t.Notes,t.Position,t.ParentId))), "Metadados alterados.");
            Require(before.Sessions.SequenceEqual(after.Sessions) && Json(before.Cycles) == Json(after.Cycles)
                && before.Intervals.SequenceEqual(after.Intervals), "Sessões, revisões ou configurações alteradas.");
            Require(after.Activities.Count == before.Activities.Count + 1 && after.Activities[0].PlanId == p, "Deve haver só uma atividade no edital correto.");
            await store.SetDisciplinePreviouslyStudiedAsync(d, true);
            Require(Json(await store.ReadAsync()) == Json(after), "Repetição gerou alterações.");
            var reopened = new StudyStore(store.DatabasePath); await reopened.InitializeAsync();
            Require(Json(await reopened.ReadAsync()) == Json(after), "Marcação não persistiu.");
            var topic = after.Topics.Single(t => t.Id == child);
            await reopened.SaveTopicAsync(topic.Id, d, group, topic.Name, TopicStatus.InProgress, topic.Priority, topic.Notes);
            var edited = await reopened.ReadAsync();
            Require(edited.DisciplineStatus(d) == TopicStatus.InProgress && edited.Topics.All(t => t.PreviouslyStudied), "Status foi substituído pelo histórico.");
            await reopened.SetDisciplinePreviouslyStudiedAsync(d, false);
            var removed = await reopened.ReadAsync();
            Require(removed.Topics.All(t => !t.PreviouslyStudied) && removed.Topics.Select(t => t.Status).SequenceEqual(edited.Topics.Select(t => t.Status)), "Remover alterou status.");
            Require(removed.Summary().Minutes == 55 && Json(removed.Cycles) == Json(before.Cycles), "Remover alterou estudos reais.");
            await reopened.SetDisciplinePreviouslyStudiedAsync(d, false);
            Require(Json(await reopened.ReadAsync()) == Json(removed), "Remoção repetida gerou atividade.");
        });
        await Check("status e histórico agregado pelas folhas, vazio e novos conteúdos", async store =>
        {
            var (p, d, group, child) = await Seed(store);
            var empty = await store.SaveDisciplineAsync(null, p, "Vazia");
            var s = await store.ReadAsync();
            Require(s.DisciplineStatus(empty) == null, "Disciplina vazia tem status.");
            var count = s.Activities.Count;
            await store.SetDisciplinePreviouslyStudiedAsync(empty, true);
            Require((await store.ReadAsync()).Activities.Count == count, "Disciplina vazia gerou atividade.");
            foreach (var t in s.Topics.Where(t => t.ParentId != null || !s.IsGroup(t)))
                await store.SaveTopicAsync(t.Id, d, t.ParentId, t.Name, TopicStatus.NotStarted, t.Priority, t.Notes);
            Require((await store.ReadAsync()).DisciplineStatus(d) == TopicStatus.NotStarted, "Todos pendentes deveriam ser não iniciado.");
            await store.SetPreviouslyStudiedAsync(child, true); s = await store.ReadAsync();
            Require(s.DisciplineStatus(d) == TopicStatus.InProgress && s.PreviousStudyLabel(s.Disciplines.Single(x => x.Id == d)).Contains("1 de 3"), "Mistura ou contagem de folhas incorreta.");
            await store.SetDisciplinePreviouslyStudiedAsync(d, true); s = await store.ReadAsync();
            Require(s.DisciplineStatus(d) == TopicStatus.Completed && s.PreviousStudyLabel(s.Disciplines.Single(x => x.Id == d)).Contains("Todos os itens"), "Conclusão não agregou.");
            var added = await store.SaveTopicAsync(null, d, group, "Novo filho", TopicStatus.NotStarted, Priority.Medium, "");
            s = await store.ReadAsync();
            Require(!s.Topics.Single(t => t.Id == added).PreviouslyStudied && s.DisciplineStatus(d) == TopicStatus.InProgress
                && s.PreviousStudyLabel(s.Disciplines.Single(x => x.Id == d)).Contains("3 de 4"), "Novo filho herdou marcação ou foi contado com o pai.");
            await store.SetDisciplinePreviouslyStudiedAsync(d, false); s = await store.ReadAsync();
            Require(s.PreviousStudyLabel(s.Disciplines.Single(x => x.Id == d)).Contains("nenhum"), "Histórico vazio incorreto.");
        });
        await Check("isolamento, identificador inválido e rollback da transação inteira", async store =>
        {
            var (p, d, _, _) = await Seed(store);
            var other = await store.SaveDisciplineAsync(null, p, "Outra disciplina");
            var a = await store.SaveTopicAsync(null, other, null, "Não alterar", TopicStatus.InProgress, Priority.High, "preservar");
            var (_, _, _, b) = await Seed(store);
            await store.SetDisciplinePreviouslyStudiedAsync(d, true);
            var s = await store.ReadAsync();
            Require(!s.Topics.Single(t => t.Id == a).PreviouslyStudied && !s.Topics.Single(t => t.Id == b).PreviouslyStudied, "Alterou outra disciplina ou plano.");
            await Reject(() => store.SetDisciplinePreviouslyStudiedAsync(int.MaxValue, true), "válida");
            var before = Json(await store.ReadAsync());
            using (var c = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
            {
                c.Open(); using var command = c.CreateCommand();
                command.CommandText = "CREATE TRIGGER FailDiscipline BEFORE INSERT ON Activity BEGIN SELECT RAISE(ABORT,'forced'); END;";
                command.ExecuteNonQuery();
            }
            await Reject(() => store.SetDisciplinePreviouslyStudiedAsync(d, false), "não foram reinicializados");
            Require(Json(await store.ReadAsync()) == before, "Falha deixou marcações parciais.");
        });
        return passed;
    }
    private static async Task<(int Plan, int Discipline, int Group, int Child)> Seed(StudyStore store)
    {
        var p = await store.SavePlanAsync(null, "Plano de teste", "", null, "");
        var d = await store.SaveDisciplineAsync(null, p, "Disciplina de teste");
        var group = await store.SaveTopicAsync(null, d, null, "Grupo", TopicStatus.NotStarted, Priority.High, "grupo");
        var child = await store.SaveTopicAsync(null, d, group, "Filho A", TopicStatus.NotStarted, Priority.Low, "A");
        await store.SaveTopicAsync(null, d, group, "Filho B", TopicStatus.Completed, Priority.High, "B");
        await store.SaveTopicAsync(null, d, null, "Assunto simples", TopicStatus.InProgress, Priority.Medium, "simples");
        return (p,d,group,child);
    }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); }
        catch (InvalidOperationException ex) { Require(ex.Message.Contains(message), ex.Message); return; }
        throw new InvalidOperationException("A operação deveria falhar.");
    }
}
