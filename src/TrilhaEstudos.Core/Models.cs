using System.Globalization;

namespace TrilhaEstudos.Core;

public enum TopicStatus { NotStarted, InProgress, Completed }
public enum Priority { Low, Medium, High }
public enum SessionKind { Study, Review }
public enum EntityKind { Plan, Discipline, Topic, Session }

public sealed record StudyPlan(int Id, string Name, string Institution, DateOnly? ExamDate, string Notes);
public sealed record Discipline(int Id, int PlanId, string Name, int Position);
public sealed record Topic(int Id, int DisciplineId, int? ParentId, string Name,
    TopicStatus Status, Priority Priority, string Notes, int Position, bool PreviouslyStudied = false);
public sealed record StudySession(int Id, int TopicId, SessionKind Kind, DateOnly Date,
    int Minutes, string Notes, int? CycleId);
public sealed record ReviewCycle(int Id, int TopicId, DateOnly BaseDate, int? AnchorSessionId,
    int[] Intervals, bool Superseded);
public sealed record Activity(int Id, int? PlanId, DateTimeOffset Timestamp, string Description);
public sealed record ReviewDue(int CycleId, int TopicId, string Path, DateOnly Date, int Step, int Total)
{
    public string Label => $"{Step}/{Total}";
}
public sealed record TopicFilter(int? PlanId = null, int? DisciplineId = null,
    TopicStatus? Status = null, Priority? Priority = null, bool PendingOnly = false, string Search = "");
public sealed record ProgressSummary(int NotStarted, int InProgress, int Completed, long Minutes)
{
    public int Total => NotStarted + InProgress + Completed;
    public double Percent => Total == 0 ? 0 : 100.0 * Completed / Total;
}
public sealed record DeleteImpact(string Name, int Disciplines, int Topics, int Sessions, int Cycles)
{
    public string Message => $"Excluir “{Name}”?\n\nSerão removidos: {Disciplines} disciplina(s), " +
        $"{Topics} assunto(s)/subassunto(s), {Sessions} sessão(ões) e {Cycles} ciclo(s) de revisão.\n\n" +
        "Esta ação não pode ser desfeita.";
}

public static class Labels
{
    public static string Text(this TopicStatus value) => value switch
    {
        TopicStatus.NotStarted => "Não iniciado", TopicStatus.InProgress => "Em andamento", _ => "Concluído"
    };
    public static string Text(this Priority value) => value switch
    {
        Priority.Low => "Baixa", Priority.Medium => "Média", _ => "Alta"
    };
    public static string Text(this SessionKind value) => value == SessionKind.Study ? "Estudo" : "Revisão";
    public static string Duration(long minutes) => $"{minutes / 60}h {minutes % 60:00}min";
}

public sealed class StudySnapshot
{
    public IReadOnlyList<StudyPlan> Plans { get; init; } = [];
    public IReadOnlyList<Discipline> Disciplines { get; init; } = [];
    public IReadOnlyList<Topic> Topics { get; init; } = [];
    public IReadOnlyList<StudySession> Sessions { get; init; } = [];
    public IReadOnlyList<ReviewCycle> Cycles { get; init; } = [];
    public IReadOnlyList<Activity> Activities { get; init; } = [];
    public int[] Intervals { get; init; } = [1, 7, 30];

    public int PlanIdOf(Topic topic) => Disciplines.Single(d => d.Id == topic.DisciplineId).PlanId;
    public string PathOf(Topic topic)
    {
        var discipline = Disciplines.Single(d => d.Id == topic.DisciplineId);
        var plan = Plans.Single(p => p.Id == discipline.PlanId);
        var parent = topic.ParentId is int id ? Topics.Single(t => t.Id == id).Name + " › " : "";
        return $"{plan.Name} › {discipline.Name} › {parent}{topic.Name}";
    }
    public bool IsGroup(Topic topic) => Topics.Any(t => t.ParentId == topic.Id);
    public string PreviousStudyLabel(Topic topic)
    {
        var children = Topics.Where(t => t.ParentId == topic.Id).ToArray();
        if (children.Length == 0) return topic.PreviouslyStudied ? "Estudado anteriormente" : "Sem marcação de estudo anterior";
        var marked = children.Count(t => t.PreviouslyStudied);
        return marked == 0 ? "Estudo anterior: nenhum subassunto marcado"
            : marked == children.Length ? "Estudado anteriormente • Todos os subassuntos"
            : $"Estudo anterior: {marked} de {children.Length} subassuntos";
    }
    public IEnumerable<int> TopicFamily(int id) => Topics.Where(t => t.Id == id || t.ParentId == id).Select(t => t.Id);
    public long MinutesFor(int id)
    {
        var ids = TopicFamily(id).ToHashSet();
        return Sessions.Where(s => ids.Contains(s.TopicId)).Sum(s => (long)s.Minutes);
    }
    public ProgressSummary Summary(int? planId = null)
    {
        var topics = Topics.Where(t => planId == null || PlanIdOf(t) == planId).ToArray();
        var leaves = topics.Where(t => !IsGroup(t)).ToArray();
        var ids = topics.Select(t => t.Id).ToHashSet();
        return new(leaves.Count(t => t.Status == TopicStatus.NotStarted),
            leaves.Count(t => t.Status == TopicStatus.InProgress), leaves.Count(t => t.Status == TopicStatus.Completed),
            Sessions.Where(s => ids.Contains(s.TopicId)).Sum(s => (long)s.Minutes));
    }
    public IEnumerable<Topic> Find(TopicFilter filter)
    {
        var compare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
        return Topics.Where(t => (filter.PlanId == null || PlanIdOf(t) == filter.PlanId)
            && (filter.DisciplineId == null || t.DisciplineId == filter.DisciplineId)
            && (filter.Status == null || t.Status == filter.Status)
            && (filter.Priority == null || t.Priority == filter.Priority)
            && (!filter.PendingOnly || t.Status != TopicStatus.Completed)
            && compare.IndexOf(PathOf(t) + " " + t.Notes, filter.Search.Trim(),
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .OrderBy(PathOf, StringComparer.CurrentCultureIgnoreCase);
    }
    public ReviewDue? DueFor(ReviewCycle cycle)
    {
        if (cycle.Superseded) return null;
        var reviews = Sessions.Where(s => s.CycleId == cycle.Id && s.Kind == SessionKind.Review)
            .OrderBy(s => s.Date).ThenBy(s => s.Id).ToArray();
        if (reviews.Length >= cycle.Intervals.Length) return null;
        var from = reviews.Length == 0 ? cycle.BaseDate : reviews[^1].Date;
        var topic = Topics.Single(t => t.Id == cycle.TopicId);
        return new(cycle.Id, topic.Id, PathOf(topic), from.AddDays(cycle.Intervals[reviews.Length]),
            reviews.Length + 1, cycle.Intervals.Length);
    }
    public IEnumerable<ReviewDue> PendingReviews(int? planId = null) => Cycles
        .Where(c => planId == null || PlanIdOf(Topics.Single(t => t.Id == c.TopicId)) == planId)
        .Select(DueFor).OfType<ReviewDue>().OrderBy(r => r.Date).ThenBy(r => r.Path);

    public DeleteImpact Impact(EntityKind kind, int id)
    {
        var disciplines = kind switch
        {
            EntityKind.Plan => Disciplines.Where(d => d.PlanId == id).Select(d => d.Id).ToHashSet(),
            EntityKind.Discipline => new HashSet<int> { id }, _ => []
        };
        var topics = kind switch
        {
            EntityKind.Plan or EntityKind.Discipline => Topics.Where(t => disciplines.Contains(t.DisciplineId)).Select(t => t.Id).ToHashSet(),
            EntityKind.Topic => TopicFamily(id).ToHashSet(), _ => []
        };
        var name = kind switch
        {
            EntityKind.Plan => Plans.Single(p => p.Id == id).Name,
            EntityKind.Discipline => Disciplines.Single(d => d.Id == id).Name,
            EntityKind.Topic => Topics.Single(t => t.Id == id).Name,
            _ => $"Sessão de {Sessions.Single(s => s.Id == id).Date:dd/MM/yyyy}"
        };
        return new(name, disciplines.Count, topics.Count,
            kind == EntityKind.Session ? 1 : Sessions.Count(s => topics.Contains(s.TopicId)),
            Cycles.Count(c => topics.Contains(c.TopicId)));
    }
}
