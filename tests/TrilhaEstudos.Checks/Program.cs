using Microsoft.Data.Sqlite;
using System.IO;
using TrilhaEstudos.Core;

if (args.Length == 4 && args[0] == "--packages")
{
    await PackageChecks.RunAsync(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
    return 0;
}
if (args.Length == 2 && args[0] == "--verify-updated-data")
{
    await PackageChecks.VerifyAsync(Path.GetFullPath(args[1]));
    return 0;
}

var today = new DateOnly(2026, 10, 18);
var root = Path.Combine(Path.GetTempPath(), "TrilhaEstudos.Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
var failed = 0;
async Task Check(string name, Func<StudyStore, Task> test)
{
    var path = Path.Combine(root, Guid.NewGuid().ToString("N"), "test.db");
    var store = new StudyStore(path, () => today);
    try
    {
        await store.InitializeAsync(); await test(store); passed++; Console.WriteLine($"OK   {name}");
    }
    catch (Exception ex) { failed++; Console.WriteLine($"ERRO {name}\n{ex}"); }
}
void Expect(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
async Task Reject(Func<Task> operation, string fragment)
{
    try { await operation(); }
    catch (InvalidOperationException ex)
    {
        Expect(ex.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase), $"Mensagem inesperada: {ex.Message}"); return;
    }
    throw new Exception("A operação deveria ter sido rejeitada.");
}
async Task<(int Plan, int Discipline, int Topic)> Seed(StudyStore store, string name = "Constituição")
{
    var p = await store.SavePlanAsync(null, "Meu edital", "Minha instituição", today.AddDays(-10), "Observações");
    var d = await store.SaveDisciplineAsync(null, p, "Direito");
    var t = await store.SaveTopicAsync(null, d, null, name, TopicStatus.NotStarted, Priority.Medium, "");
    return (p, d, t);
}

await Check("Inicialização vazia e reabertura", async store =>
{
    var s = await store.ReadAsync();
    Expect(s.Plans.Count == 0 && s.Topics.Count == 0 && s.Sessions.Count == 0 && s.Activities.Count == 0, "O banco deve iniciar sem conteúdo.");
    Expect(s.Summary().Percent == 0 && s.Intervals.SequenceEqual([1, 7, 30]), "Padrões incorretos.");
    var seeded = await Seed(store);
    var reopened = new StudyStore(store.DatabasePath, () => today);
    await reopened.InitializeAsync(); s = await reopened.ReadAsync();
    Expect(s.Plans.Single().Id == seeded.Plan && s.Topics.Single().Name == "Constituição", "Dados não persistidos.");
});
await Check("Cadastro, edição, validação e rollback", async store =>
{
    var (p, d, t) = await Seed(store);
    await store.SavePlanAsync(p, "  Novo nome  ", "", null, "Nova nota");
    await store.SaveDisciplineAsync(d, p, "Disciplina editada");
    await store.SaveTopicAsync(t, d, null, "Novo assunto", TopicStatus.InProgress, Priority.High, "Nota");
    var before = await store.ReadAsync();
    await Reject(() => store.SavePlanAsync(p, "   ", "", null, ""), "nome");
    await Reject(() => store.SaveTopicAsync(t, d, null, "x", (TopicStatus)99, Priority.High, ""), "inválidos");
    var after = await store.ReadAsync();
    Expect(after.Plans.Single().Name == "Novo nome" && after.Topics.Single().Status == TopicStatus.InProgress, "Edições incorretas.");
    Expect(before.Activities.Count == after.Activities.Count && after.Topics.Single().Name == "Novo assunto", "Falha parcial de gravação.");
});
await Check("Reordenação entre irmãos e limites", async store =>
{
    var (p, d, t) = await Seed(store);
    var d2 = await store.SaveDisciplineAsync(null, p, "Outra disciplina");
    await store.MoveAsync(EntityKind.Discipline, d2, -1);
    var t2 = await store.SaveTopicAsync(null, d, null, "Outro", TopicStatus.NotStarted, Priority.Low, "");
    await store.MoveAsync(EntityKind.Topic, t2, -1);
    await store.MoveAsync(EntityKind.Topic, t2, -1);
    var s = await store.ReadAsync();
    Expect(s.Disciplines.First().Id == d2 && s.Topics.First().Id == t2, "A ordem não foi alterada.");
    var c1 = await store.SaveTopicAsync(null, d, t, "Filho A", TopicStatus.NotStarted, Priority.Low, "");
    var c2 = await store.SaveTopicAsync(null, d, t, "Filho B", TopicStatus.NotStarted, Priority.Low, "");
    await store.MoveAsync(EntityKind.Topic, c2, -1);
    s = await store.ReadAsync(); Expect(s.Topics.Where(x => x.ParentId == t).First().Id == c2, "Subassuntos fora de ordem.");
});
await Check("Progresso por folhas, grupo misto e tempo sem duplicação", async store =>
{
    var (_, d, t) = await Seed(store);
    await store.SaveSessionAsync(null, t, SessionKind.Study, today.AddDays(-10), 60, "Sessão do pai");
    var c1 = await store.SaveTopicAsync(null, d, t, "Filho 1", TopicStatus.Completed, Priority.High, "");
    var c2 = await store.SaveTopicAsync(null, d, t, "Filho 2", TopicStatus.NotStarted, Priority.Low, "");
    await store.SaveSessionAsync(null, c1, SessionKind.Study, today, 30, "Sessão do filho");
    var s = await store.ReadAsync();
    Expect(s.Topics.Single(x => x.Id == t).Status == TopicStatus.InProgress, "Grupo misto incorreto.");
    Expect(s.Summary().Total == 2 && s.Summary().Percent == 50, "Pai contado em duplicidade.");
    Expect(s.Summary().Minutes == 90 && s.MinutesFor(t) == 90, "Tempo duplicado ou perdido.");
    await store.SaveTopicAsync(t, d, null, "Grupo renomeado", TopicStatus.InProgress, Priority.High, "nota", false);
    s = await store.ReadAsync(); Expect(s.Topics.Single(x => x.Id == c1).Status == TopicStatus.Completed, "Editar metadados mudou filhos.");
    await store.SaveTopicAsync(t, d, null, "Grupo", TopicStatus.Completed, Priority.High, "");
    s = await store.ReadAsync(); Expect(s.Summary().Completed == 2 && s.Cycles.Count == 2, "Status em lote ou ciclos não preservados.");
});
await Check("Remover último subassunto conserva status agregado", async store =>
{
    var (_, d, t) = await Seed(store);
    var child = await store.SaveTopicAsync(null, d, t, "Filho", TopicStatus.Completed, Priority.Medium, "");
    await store.DeleteAsync(EntityKind.Topic, child);
    var s = await store.ReadAsync(); Expect(s.Topics.Single().Status == TopicStatus.Completed && s.Summary().Total == 1, "Status do pai não foi preservado.");
});
await Check("Hierarquia rejeita terceiro nível e vínculo cruzado", async store =>
{
    var (p, d, t) = await Seed(store);
    var child = await store.SaveTopicAsync(null, d, t, "Filho", TopicStatus.NotStarted, Priority.Low, "");
    var d2 = await store.SaveDisciplineAsync(null, p, "Outra");
    await Reject(() => store.SaveTopicAsync(null, d2, t, "Inválido", TopicStatus.NotStarted, Priority.Low, ""), "mesma disciplina");
    await Reject(() => store.SaveTopicAsync(null, d, child, "Neto", TopicStatus.NotStarted, Priority.Low, ""), "subassunto");
    Expect((await store.ReadAsync()).Topics.Count == 2, "Cadastros inválidos foram salvos.");
});
await Check("Busca com acentos e filtros combinados", async store =>
{
    var (p, d, t) = await Seed(store);
    await store.SaveTopicAsync(t, d, null, "Constituição", TopicStatus.InProgress, Priority.High, "Administração pública");
    await store.SaveTopicAsync(null, d, null, "Outro", TopicStatus.Completed, Priority.Low, "");
    var s = await store.ReadAsync();
    Expect(s.Find(new(p, d, TopicStatus.InProgress, Priority.High, true, "CONSTITUICAO")).Single().Id == t, "Busca normalizada incorreta.");
    Expect(s.Find(new(Search: "administracao")).Count() == 1, "Observações não pesquisadas.");
    Expect(!s.Find(new(Status: TopicStatus.Completed, PendingOnly: true)).Any(), "Filtros não foram combinados.");
});
await Check("Primeiro registro inicia ciclo; retroativo não reinicia", async store =>
{
    var (_, _, t) = await Seed(store);
    var first = await store.SaveSessionAsync(null, t, SessionKind.Study, today.AddDays(-10), 40, "Primeiro registro");
    await store.SaveSessionAsync(null, t, SessionKind.Study, today.AddDays(-30), 20, "Retroativo");
    var s = await store.ReadAsync();
    Expect(s.Cycles.Single().AnchorSessionId == first && s.PendingReviews().Single().Date == today.AddDays(-9), "Ciclo reiniciado por registro retroativo.");
});
await Check("Revisões atrasadas deslocam próximas datas e encerram", async store =>
{
    var (_, _, t) = await Seed(store);
    await store.SaveSessionAsync(null, t, SessionKind.Study, today.AddDays(-40), 20, "");
    await store.SaveSessionAsync(null, t, SessionKind.Review, today.AddDays(-35), 10, "");
    var s = await store.ReadAsync(); Expect(s.PendingReviews().Single().Date == today.AddDays(-28), "Prazo não partiu da revisão real.");
    await store.SaveSessionAsync(null, t, SessionKind.Review, today.AddDays(-20), 10, "");
    s = await store.ReadAsync(); Expect(s.PendingReviews().Single().Date == today.AddDays(10), "Terceiro intervalo incorreto.");
    await store.SaveSessionAsync(null, t, SessionKind.Review, today, 10, "");
    await store.SaveSessionAsync(null, t, SessionKind.Study, today, 10, "");
    var extra = await store.SaveSessionAsync(null, t, SessionKind.Review, today, 10, "Extra");
    s = await store.ReadAsync(); Expect(!s.PendingReviews().Any() && s.Sessions.Single(x => x.Id == extra).CycleId == null, "Ciclo encerrado foi reaberto.");
});
await Check("Configuração só afeta novos ciclos", async store =>
{
    var (_, d, t) = await Seed(store);
    await store.SaveSessionAsync(null, t, SessionKind.Study, today, 10, "");
    await store.SaveIntervalsAsync("2, 10");
    var other = await store.SaveTopicAsync(null, d, null, "Novo ciclo", TopicStatus.NotStarted, Priority.Medium, "");
    await store.SaveSessionAsync(null, other, SessionKind.Study, today, 10, "");
    await Reject(() => store.SaveIntervalsAsync("1,,30"), "intervalos");
    var s = await store.ReadAsync();
    Expect(s.Cycles.Single(c => c.TopicId == t).Intervals.SequenceEqual([1, 7, 30]), "Ciclo antigo foi alterado.");
    Expect(s.Cycles.Single(c => c.TopicId == other).Intervals.SequenceEqual([2, 10]) && s.Intervals.SequenceEqual([2, 10]), "Configuração não persistida.");
});
await Check("Editar e excluir sessão inicial preserva ciclo", async store =>
{
    var (_, _, t) = await Seed(store);
    var anchor = await store.SaveSessionAsync(null, t, SessionKind.Study, today.AddDays(-20), 20, "");
    await store.SaveSessionAsync(anchor, t, SessionKind.Study, today.AddDays(-19), 25, "Corrigida");
    Expect((await store.ReadAsync()).PendingReviews().Single().Date == today.AddDays(-18), "Data-base não atualizada.");
    await store.SaveSessionAsync(null, t, SessionKind.Review, today.AddDays(-10), 10, "");
    await Reject(() => store.SaveSessionAsync(anchor, t, SessionKind.Study, today, 25, ""), "depois");
    await store.DeleteAsync(EntityKind.Session, anchor);
    var s = await store.ReadAsync(); Expect(s.Cycles.Single().AnchorSessionId == null && s.Cycles.Single().BaseDate == today.AddDays(-19), "Base apagada ao remover sessão inicial.");
});
await Check("Editar/excluir revisões recalcula sequência cronológica", async store =>
{
    var (_, _, t) = await Seed(store);
    await store.SaveSessionAsync(null, t, SessionKind.Study, today.AddDays(-40), 20, "");
    var r1 = await store.SaveSessionAsync(null, t, SessionKind.Review, today.AddDays(-30), 10, "");
    var r2 = await store.SaveSessionAsync(null, t, SessionKind.Review, today.AddDays(-20), 10, "");
    await store.SaveSessionAsync(r2, t, SessionKind.Review, today.AddDays(-15), 15, "Corrigida");
    Expect((await store.ReadAsync()).PendingReviews().Single().Date == today.AddDays(15), "Edição não recalculou prazo.");
    await store.DeleteAsync(EntityKind.Session, r1);
    var s = await store.ReadAsync(); Expect(s.PendingReviews().Single().Step == 2 && s.PendingReviews().Single().Date == today.AddDays(-8), "Exclusão não reabriu etapa.");
    await Reject(() => store.SaveSessionAsync(r2, t, SessionKind.Review, today.AddDays(-41), 15, ""), "anteceder");
});
await Check("Reinício arquiva ciclo antigo e evita pendências duplicadas", async store =>
{
    var (_, _, t) = await Seed(store);
    await store.SaveIntervalsAsync("1");
    await store.SaveSessionAsync(null, t, SessionKind.Study, today.AddDays(-10), 20, "");
    await Reject(() => store.StartCycleAsync(t), "atual");
    var review = await store.SaveSessionAsync(null, t, SessionKind.Review, today.AddDays(-9), 10, "");
    await store.StartCycleAsync(t);
    await store.DeleteAsync(EntityKind.Session, review);
    var s = await store.ReadAsync();
    Expect(s.Cycles.Count == 2 && s.Cycles.First().Superseded && s.PendingReviews().Count() == 1, "Ciclo anterior reabriu pendência duplicada.");
    Expect(s.PendingReviews().Single().Date == today.AddDays(1), "Reinício não usou hoje.");
});
await Check("Validações de sessões não gravam dados parciais", async store =>
{
    var (_, _, t) = await Seed(store);
    await Reject(() => store.SaveSessionAsync(null, t, SessionKind.Study, today, 0, ""), "duração");
    await Reject(() => store.SaveSessionAsync(null, t, SessionKind.Study, today.AddDays(1), 20, ""), "futuro");
    await Reject(() => store.SaveSessionAsync(null, t, SessionKind.Study, today, 1441, ""), "duração");
    var s = await store.ReadAsync(); Expect(s.Sessions.Count == 0 && s.Cycles.Count == 0, "Dados parciais criados.");
    var sid = await store.SaveSessionAsync(null, t, SessionKind.Review, today, 15, "Revisão histórica sem ciclo");
    s = await store.ReadAsync(); Expect(s.Sessions.Single().CycleId == null && s.Cycles.Count == 0, "Revisão sem estudo criou ciclo.");
    await Reject(() => store.SaveSessionAsync(sid, t, SessionKind.Study, today, 20, ""), "tipo");
});
await Check("Exclusão em cascata, impacto e integridade referencial", async store =>
{
    var (p, d, t) = await Seed(store);
    var child = await store.SaveTopicAsync(null, d, t, "Filho", TopicStatus.NotStarted, Priority.Low, "");
    await store.SaveSessionAsync(null, t, SessionKind.Study, today.AddDays(-5), 30, "");
    await store.SaveSessionAsync(null, t, SessionKind.Review, today, 10, "");
    await store.SaveSessionAsync(null, child, SessionKind.Study, today, 20, "");
    var impact = (await store.ReadAsync()).Impact(EntityKind.Plan, p);
    Expect(impact.Disciplines == 1 && impact.Topics == 2 && impact.Sessions == 3 && impact.Cycles == 2, "Impacto incompleto.");
    await store.DeleteAsync(EntityKind.Plan, p);
    var s = await store.ReadAsync();
    Expect(s.Plans.Count + s.Disciplines.Count + s.Topics.Count + s.Sessions.Count + s.Cycles.Count == 0, "Sobrou conteúdo órfão.");
    using var connection = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"); connection.Open();
    using var cmd = connection.CreateCommand(); cmd.CommandText = "PRAGMA foreign_key_check";
    using var reader = cmd.ExecuteReader(); Expect(!reader.Read(), "Chaves estrangeiras inválidas.");
});
await Check("Falha real de escrita causa rollback e mensagem compreensível", async store =>
{
    var (p, _, _) = await Seed(store);
    using (var c = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
    {
        c.Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TRIGGER TestWriteFailure BEFORE INSERT ON Activity BEGIN SELECT RAISE(ABORT,'forced'); END;"; cmd.ExecuteNonQuery();
    }
    await Reject(() => store.SavePlanAsync(p, "Não deve salvar", "", null, ""), "não foram reinicializados");
    Expect((await store.ReadAsync()).Plans.Single().Name == "Meu edital", "Transação parcialmente salva após erro.");
});
await Check("Versão futura do esquema é preservada", async store =>
{
    await Seed(store);
    using (var c = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
    {
        c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA user_version=99"; cmd.ExecuteNonQuery();
    }
    await Reject(() => store.InitializeAsync(), "mais recente");
    Expect((await store.ReadAsync()).Plans.Count == 1, "Banco incompatível foi apagado.");
});
await Check("Atividades filtradas e resumo por edital", async store =>
{
    var (p, _, t) = await Seed(store);
    var second = await store.SavePlanAsync(null, "Edital vazio", "", null, "");
    await store.SaveSessionAsync(null, t, SessionKind.Study, today, 45, "");
    var s = await store.ReadAsync();
    Expect(s.Summary(p).Minutes == 45 && s.Summary(second).Total == 0 && s.Summary(second).Percent == 0, "Resumo por edital incorreto.");
    Expect(s.Activities.Where(a => a.PlanId == p).Take(10).Count() == 4, "Atividades por edital incorretas.");
});

try { passed += await UpdateChecks.RunAsync(root); }
catch (Exception ex) { failed++; Console.WriteLine($"ERRO Atualizações\n{ex}"); }
try { passed += await LatestReleaseChecks.RunAsync(root); }
catch (Exception ex) { failed++; Console.WriteLine($"ERRO Consulta latest\n{ex}"); }
var uiIndex = Array.IndexOf(args, "--ui");
if (uiIndex >= 0)
{
    var output = uiIndex + 1 < args.Length ? args[uiIndex + 1] : Path.Combine(root, "captures");
    try
    {
        await UiChecks.RunAsync(root, output); passed++; Console.WriteLine("OK   Interface WPF: telas, tamanho mínimo, formulário e bindings");
    }
    catch (Exception ex) { failed++; Console.WriteLine($"ERRO Interface WPF\n{ex}"); }
}
Console.WriteLine($"\n{passed} cenários aprovados; {failed} falhas.");
// Apenas a pasta temporária exclusiva criada por esta execução pode ser removida.
if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
    && Path.GetFileName(root).StartsWith("TrilhaEstudos.Checks-", StringComparison.Ordinal))
{
    try { Directory.Delete(root, true); }
    catch (IOException) { Console.WriteLine($"Pasta temporária preservada: {root}"); }
}
return failed == 0 ? 0 : 1;
