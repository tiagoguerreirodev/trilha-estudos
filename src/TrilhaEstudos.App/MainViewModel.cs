using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TrilhaEstudos.Core;

namespace TrilhaEstudos.App;

public sealed class MainViewModel : Observable
{
    private readonly StudyStore _store;
    private StudySnapshot _snapshot = new();
    private readonly DispatcherTimer _dayTimer;
    private DateOnly _lastDay = DateOnly.FromDateTime(DateTime.Now);
    private bool _refreshing;
    private bool _ready;
    public MainViewModel(StudyStore store, IAppUpdater? updater = null)
    {
        _store = store;
        Updates = new UpdateViewModel(updater ?? new UnavailableAppUpdater(), store, () => !IsBusy);
        ShowUpdatesCommand = Command(() => { PageIndex = 5; return Task.CompletedTask; });
        NewPlanCommand = Command(NewPlan);
        NewDisciplineCommand = Command(NewDiscipline, () => ContextPlanId != null);
        NewTopicCommand = Command(() => NewTopic(false), () => ContextDisciplineId != null);
        NewSubtopicCommand = Command(() => NewTopic(true), () => CurrentTopic is { ParentId: null });
        EditContentCommand = Command(EditContent, () => SelectedNode != null);
        DeleteContentCommand = Command(DeleteContent, () => SelectedNode != null);
        MoveUpCommand = Command(() => Move(-1), () => CanMove(-1));
        MoveDownCommand = Command(() => Move(1), () => CanMove(1));
        NewSessionCommand = Command(NewSession, () => _snapshot.Topics.Count > 0);
        EditSessionCommand = Command(EditSession, () => SelectedSession != null);
        DeleteSessionCommand = Command(DeleteSession, () => SelectedSession != null);
        RestartCycleCommand = Command(RestartCycle, () => CurrentTopic != null && !_snapshot.PendingReviews().Any(r => r.TopicId == CurrentTopic.Id));
        OpenTopicCommand = Command(OpenTopic, () => PageIndex == 2 ? SelectedTopicRow != null : SelectedReview != null);
        PendingCommand = Command(() =>
        {
            _pendingOnly = true; _search = ""; _selectedStatus = StatusChoices[0]; _selectedPriority = PriorityChoices[0];
            _selectedDiscipline = DisciplineChoices[0];
            Raise(nameof(PendingOnly)); Raise(nameof(Search)); Raise(nameof(SelectedStatus)); Raise(nameof(SelectedPriority));
            PageIndex = 2; RefreshViews(); return Task.CompletedTask;
        });
        ClearFiltersCommand = Command(() =>
        {
            _search = ""; _pendingOnly = false; _selectedStatus = StatusChoices[0]; _selectedPriority = PriorityChoices[0];
            _selectedDiscipline = DisciplineChoices[0];
            Raise(nameof(Search)); Raise(nameof(PendingOnly)); Raise(nameof(SelectedStatus)); Raise(nameof(SelectedPriority)); Raise(nameof(SelectedDiscipline));
            RefreshViews(); return Task.CompletedTask;
        });
        SettingsCommand = Command(Settings);
        RetryCommand = new AsyncCommand(InitializeAsync, () => !IsBusy);
        _dayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _dayTimer.Tick += (_, _) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (_lastDay == today) return;
            _lastDay = today; RefreshViews();
        };
        _dayTimer.Start();
    }
    private AsyncCommand Command(Func<Task> action, Func<bool>? condition = null) =>
        new(() => Run(action), () => _ready && !IsBusy && !Updates.IsApplying && (condition?.Invoke() ?? true));
    private async Task Run(Func<Task> action)
    {
        IsBusy = true; Error = "";
        try { await action(); }
        catch (Exception ex) { Error = Friendly(ex); }
        finally { IsBusy = false; }
    }
    private static string Friendly(Exception ex) => ex is InvalidOperationException ? ex.Message :
        "Não foi possível concluir a operação. Seus dados foram preservados. Tente novamente.";
    public async Task InitializeAsync()
    {
        await Run(async () => { await _store.InitializeAsync(); await Reload(); _ready = true; Raise(nameof(NeedsInitialization)); });
        if (_ready) _ = Updates.CheckOnStartupAsync();
    }
    public bool NeedsInitialization => !_ready && !IsBusy;
    private bool _busy;
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(WorkStatus)); Raise(nameof(NeedsInitialization)); } }
    public string WorkStatus => IsBusy ? "Processando…" : "Dados locais • salvamento automático";
    private string _error = "";
    public string Error { get => _error; private set => Set(ref _error, value); }
    public string DatabasePath => _store.DatabasePath;
    public UpdateViewModel Updates { get; }
    public ICommand ShowUpdatesCommand { get; }
    public string IntervalsText => string.Join(", ", _snapshot.Intervals) + " dias";
    private int _pageIndex;
    public int PageIndex { get => _pageIndex; set { if (Set(ref _pageIndex, value)) Raise(nameof(PageTitle)); } }
    public string PageTitle => PageIndex switch { 0 => "Seu estudo em perspectiva", 1 => "Editais e conteúdo", 2 => "Encontre o próximo assunto", 3 => "Histórico de estudo", 4 => "Revisões", _ => "Configurações" };
    public IReadOnlyList<Choice> PlanChoices { get; private set; } = [new(-1, "Todos os editais")];
    private Choice? _selectedPlan = new(-1, "Todos os editais");
    public Choice? SelectedPlan
    {
        get => _selectedPlan;
        set
        {
            if (!Set(ref _selectedPlan, value) || _refreshing) return;
            _selectedDiscipline = new(-1, "Todas as disciplinas"); RefreshViews();
        }
    }
    private int? PlanFilter => SelectedPlan is { Id: >= 0 } p ? p.Id : null;
    public IReadOnlyList<Choice> DisciplineChoices { get; private set; } = [new(-1, "Todas as disciplinas")];
    private Choice? _selectedDiscipline = new(-1, "Todas as disciplinas");
    public Choice? SelectedDiscipline { get => _selectedDiscipline; set { if (Set(ref _selectedDiscipline, value) && !_refreshing) RefreshRows(); } }
    public IReadOnlyList<Choice> StatusChoices { get; } = [new(-1, "Todos os status"), new(0, "Não iniciado"), new(1, "Em andamento"), new(2, "Concluído")];
    public IReadOnlyList<Choice> PriorityChoices { get; } = [new(-1, "Todas as prioridades"), new(0, "Baixa"), new(1, "Média"), new(2, "Alta")];
    private Choice? _selectedStatus = new(-1, "Todos os status");
    public Choice? SelectedStatus { get => _selectedStatus; set { if (Set(ref _selectedStatus, value) && !_refreshing) RefreshRows(); } }
    private Choice? _selectedPriority = new(-1, "Todas as prioridades");
    public Choice? SelectedPriority { get => _selectedPriority; set { if (Set(ref _selectedPriority, value) && !_refreshing) RefreshRows(); } }
    private string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value)) RefreshRows(); } }
    private bool _pendingOnly;
    public bool PendingOnly { get => _pendingOnly; set { if (Set(ref _pendingOnly, value)) RefreshRows(); } }
    public IReadOnlyList<ContentNode> Tree { get; private set; } = [];
    private ContentNode? _selectedNode;
    public ContentNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!Set(ref _selectedNode, value)) return;
            RefreshSelection();
        }
    }
    private Topic? CurrentTopic => SelectedNode?.Kind == EntityKind.Topic ? _snapshot.Topics.SingleOrDefault(t => t.Id == SelectedNode.Id) : null;
    private int? ContextDisciplineId => SelectedNode?.Kind switch
    {
        EntityKind.Discipline => SelectedNode.Id, EntityKind.Topic => CurrentTopic?.DisciplineId, _ => null
    };
    private int? ContextPlanId => SelectedNode?.Kind switch
    {
        EntityKind.Plan => SelectedNode.Id,
        EntityKind.Discipline or EntityKind.Topic => _snapshot.Disciplines.SingleOrDefault(d => d.Id == ContextDisciplineId)?.PlanId,
        _ => PlanFilter
    };
    public string SelectionTitle { get; private set; } = "Comece pelo seu edital";
    public string SelectionDetails { get; private set; } = "Crie um edital e adicione disciplinas. Depois, cadastre os assuntos que deseja estudar.";
    public IReadOnlyList<SessionRow> SelectionSessions { get; private set; } = [];
    public string SelectionHistoryHint => SelectionSessions.Count == 0 ? "Ainda não há sessões neste assunto." : "Últimas sessões do assunto e seus subassuntos. O histórico completo permite editar e excluir.";
    public bool HasSelectedTopic => CurrentTopic != null;
    public bool Empty => _snapshot.Plans.Count == 0;
    public bool NoTopics => _snapshot.Topics.Count == 0;
    public string ResultHint => $"{TopicRows.Count} resultado(s). Grupos são identificados; o percentual do painel conta somente itens finais.";
    public IReadOnlyList<TopicRow> TopicRows { get; private set; } = [];
    private TopicRow? _selectedTopicRow;
    public TopicRow? SelectedTopicRow { get => _selectedTopicRow; set => Set(ref _selectedTopicRow, value); }
    public IReadOnlyList<SessionRow> SessionRows { get; private set; } = [];
    private SessionRow? _selectedSession;
    public SessionRow? SelectedSession { get => _selectedSession; set => Set(ref _selectedSession, value); }
    public IReadOnlyList<ReviewRow> ReviewRows { get; private set; } = [];
    private ReviewRow? _selectedReview;
    public ReviewRow? SelectedReview { get => _selectedReview; set => Set(ref _selectedReview, value); }
    public IReadOnlyList<ReviewRow> UrgentReviews { get; private set; } = [];
    public IReadOnlyList<string> RecentActivities { get; private set; } = [];
    public ProgressSummary Summary { get; private set; } = new(0, 0, 0, 0);
    public string PercentText => $"{Summary.Percent:0.#}% concluído";
    public string TimeText => Labels.Duration(Summary.Minutes);
    public string ReviewHint => ReviewRows.Count == 0 ? "Nenhuma revisão pendente. Registre o primeiro estudo de um assunto para iniciar um ciclo." : $"{ReviewRows.Count} ciclo(s) com próxima revisão. Datas calculadas após cada etapa realizada.";
    public string UrgentHint => UrgentReviews.Count == 0 ? "Nenhuma revisão para hoje ou atrasada." : $"{UrgentReviews.Count} revisão(ões) para hoje ou atrasada(s). Consulte Revisões para registrar.";

    public ICommand NewPlanCommand { get; }
    public ICommand NewDisciplineCommand { get; }
    public ICommand NewTopicCommand { get; }
    public ICommand NewSubtopicCommand { get; }
    public ICommand EditContentCommand { get; }
    public ICommand DeleteContentCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand NewSessionCommand { get; }
    public ICommand EditSessionCommand { get; }
    public ICommand DeleteSessionCommand { get; }
    public ICommand RestartCycleCommand { get; }
    public ICommand PendingCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand OpenTopicCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand RetryCommand { get; }

    private async Task Reload()
    {
        _snapshot = await _store.ReadAsync();
        _refreshing = true;
        var planId = _selectedPlan?.Id ?? -1;
        PlanChoices = new[] { new Choice(-1, "Todos os editais") }.Concat(_snapshot.Plans.Select(p => new Choice(p.Id, p.Name))).ToArray();
        _selectedPlan = PlanChoices.FirstOrDefault(p => p.Id == planId) ?? PlanChoices[0];
        Raise(nameof(PlanChoices)); Raise(nameof(SelectedPlan)); _refreshing = false;
        RefreshViews();
    }
    private ContentNode TopicNode(Topic t) => new()
    {
        Id = t.Id, Kind = EntityKind.Topic, Name = t.Name,
        Detail = $"{t.Status.Text()} • {t.Priority.Text()}" + (_snapshot.IsGroup(t) ? " • Grupo" : ""),
        Children = _snapshot.Topics.Where(x => x.ParentId == t.Id).Select(TopicNode).ToList(),
        IsSelected = _selectedNode?.Kind == EntityKind.Topic && _selectedNode.Id == t.Id
    };
    private void RefreshViews()
    {
        _refreshing = true;
        DisciplineChoices = new[] { new Choice(-1, "Todas as disciplinas") }.Concat(_snapshot.Disciplines
            .Where(d => PlanFilter == null || d.PlanId == PlanFilter).Select(d => new Choice(d.Id, d.Name))).ToArray();
        _selectedDiscipline = DisciplineChoices.FirstOrDefault(d => d.Id == _selectedDiscipline?.Id) ?? DisciplineChoices[0];
        Raise(nameof(DisciplineChoices)); Raise(nameof(SelectedDiscipline));
        Tree = _snapshot.Plans.Where(p => PlanFilter == null || p.Id == PlanFilter).Select(p => new ContentNode
        {
            Id = p.Id, Kind = EntityKind.Plan, Name = p.Name, Detail = "Edital / plano",
            IsSelected = _selectedNode?.Kind == EntityKind.Plan && _selectedNode.Id == p.Id,
            Children = _snapshot.Disciplines.Where(d => d.PlanId == p.Id).Select(d => new ContentNode
            {
                Id = d.Id, Kind = EntityKind.Discipline, Name = d.Name, Detail = "Disciplina",
                IsSelected = _selectedNode?.Kind == EntityKind.Discipline && _selectedNode.Id == d.Id,
                Children = _snapshot.Topics.Where(t => t.DisciplineId == d.Id && t.ParentId == null).Select(TopicNode).ToList()
            }).ToList()
        }).ToArray();
        ContentNode? Find(IEnumerable<ContentNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)))
            .FirstOrDefault(n => n.Kind == _selectedNode?.Kind && n.Id == _selectedNode.Id);
        _selectedNode = Find(Tree); Raise(nameof(Tree)); Raise(nameof(SelectedNode));
        Summary = _snapshot.Summary(PlanFilter);
        ReviewRows = _snapshot.PendingReviews(PlanFilter).Select(ReviewDisplay).ToArray();
        UrgentReviews = ReviewRows.Where(r => r.State != "Programada").ToArray();
        SessionRows = _snapshot.Sessions.Where(s => PlanFilter == null || _snapshot.PlanIdOf(_snapshot.Topics.Single(t => t.Id == s.TopicId)) == PlanFilter).Select(SessionDisplay).ToArray();
        SelectedSession = SessionRows.FirstOrDefault(r => r.Id == SelectedSession?.Id);
        SelectedReview = ReviewRows.FirstOrDefault(r => r.TopicId == SelectedReview?.TopicId);
        RecentActivities = _snapshot.Activities.Where(a => PlanFilter == null || a.PlanId == PlanFilter).Take(10)
            .Select(a => $"{a.Timestamp.ToLocalTime():dd/MM HH:mm}  •  {a.Description}").ToArray();
        _refreshing = false;
        foreach (var property in new[] { nameof(Summary), nameof(PercentText), nameof(TimeText), nameof(ReviewRows), nameof(ReviewHint), nameof(UrgentReviews), nameof(UrgentHint), nameof(SessionRows), nameof(RecentActivities), nameof(Empty), nameof(NoTopics), nameof(IntervalsText) }) Raise(property);
        RefreshRows(); RefreshSelection(); CommandManager.InvalidateRequerySuggested();
    }
    private static IEnumerable<ContentNode> Flatten(IEnumerable<ContentNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
    private ReviewRow ReviewDisplay(ReviewDue due)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return new(due.TopicId, due.Date.ToString("dd/MM/yyyy"), due.Path, due.Label,
            due.Date < today ? "Atrasada" : due.Date == today ? "Hoje" : "Programada");
    }
    private SessionRow SessionDisplay(StudySession session) => new(session.Id, session.Date.ToString("dd/MM/yyyy"), session.Kind.Text(),
        _snapshot.PathOf(_snapshot.Topics.Single(t => t.Id == session.TopicId)), Labels.Duration(session.Minutes), session.Notes);
    private void RefreshRows()
    {
        TopicRows = _snapshot.Find(new(PlanFilter, SelectedDiscipline is { Id: >= 0 } d ? d.Id : null,
            SelectedStatus is { Id: >= 0 } st ? (TopicStatus)st.Id : null,
            SelectedPriority is { Id: >= 0 } pr ? (Priority)pr.Id : null, PendingOnly, Search))
            .Select(t => new TopicRow(t.Id, t.Name, _snapshot.PathOf(t), t.Status.Text(), t.Priority.Text(),
                Labels.Duration(_snapshot.MinutesFor(t.Id)), _snapshot.IsGroup(t) ? "Grupo" : "Item final")).ToArray();
        SelectedTopicRow = TopicRows.FirstOrDefault(t => t.Id == SelectedTopicRow?.Id);
        Raise(nameof(TopicRows)); Raise(nameof(ResultHint));
    }
    private void RefreshSelection()
    {
        SelectionSessions = [];
        if (SelectedNode == null)
        {
            SelectionTitle = Empty ? "Comece pelo seu edital" : "Escolha um item na árvore";
            SelectionDetails = "1. Crie um edital com o nome que preferir.\n2. Selecione o edital e adicione uma disciplina.\n3. Selecione a disciplina e cadastre os assuntos.\n\nTudo começa vazio e fica salvo neste computador.";
        }
        else if (SelectedNode.Kind == EntityKind.Plan)
        {
            var p = _snapshot.Plans.Single(p => p.Id == SelectedNode.Id); SelectionTitle = p.Name;
            SelectionDetails = $"Edital / plano de estudo\n{p.Institution}\nProva: {(p.ExamDate is { } date ? date.ToString("dd/MM/yyyy") : "não informada")}\n\n{p.Notes}";
        }
        else if (SelectedNode.Kind == EntityKind.Discipline)
        {
            SelectionTitle = SelectedNode.Name; SelectionDetails = "Disciplina. Use “Novo assunto” para cadastrar seu conteúdo. Selecione um assunto para registrar sessões.";
        }
        else if (CurrentTopic is { } t)
        {
            SelectionTitle = t.Name;
            var due = _snapshot.PendingReviews().SingleOrDefault(r => r.TopicId == t.Id);
            SelectionDetails = $"{_snapshot.PathOf(t)}\n\n{t.Status.Text()} • Prioridade {t.Priority.Text().ToLowerInvariant()}\nTempo: {Labels.Duration(_snapshot.MinutesFor(t.Id))}\n" +
                (due == null ? "Sem revisão pendente neste assunto.\n" : $"Próxima revisão: {due.Date:dd/MM/yyyy} • Etapa {due.Label}\n") +
                (_snapshot.IsGroup(t) ? "Grupo: o status e o tempo resumem os subassuntos; o tempo inclui sessões próprias.\n" : "") + $"\n{t.Notes}";
            var family = _snapshot.TopicFamily(t.Id).ToHashSet();
            SelectionSessions = _snapshot.Sessions.Where(s => family.Contains(s.TopicId)).Take(5).Select(SessionDisplay).ToArray();
        }
        foreach (var property in new[] { nameof(SelectionTitle), nameof(SelectionDetails), nameof(SelectionSessions), nameof(SelectionHistoryHint), nameof(HasSelectedTopic) }) Raise(property);
        CommandManager.InvalidateRequerySuggested();
    }
    private async Task ShowEditor(string title, string hint, EditorViewModel draft, Func<EditorViewModel, Task> save)
    {
        var dialog = new EditorDialog(title, hint, draft, save);
        if (dialog.ShowDialog() == true) await Reload();
    }
    private Task NewPlan() => PlanEditor(null);
    private Task PlanEditor(StudyPlan? p)
    {
        var draft = new EditorViewModel().Text("name", "Nome do edital / plano *", p?.Name ?? "")
            .Text("institution", "Instituição ou prova (opcional)", p?.Institution ?? "")
            .Date("exam", "Data da prova (opcional)", p?.ExamDate).Text("notes", "Observações (opcional)", p?.Notes ?? "", true);
        return ShowEditor(p == null ? "Novo edital" : "Editar edital", "O nome é obrigatório. Os demais campos podem ficar em branco.", draft,
            async v =>
            {
                var id = await _store.SavePlanAsync(p?.Id, v["name"].Text, v["institution"].Text, v.GetDate("exam"), v["notes"].Text);
                _selectedNode = new ContentNode { Id = id, Kind = EntityKind.Plan, Name = v["name"].Text };
                if (p == null) { _selectedPlan = new Choice(-1, "Todos os editais"); PageIndex = 1; }
            });
    }
    private Task NewDiscipline() => DisciplineEditor(null, ContextPlanId!.Value);
    private Task DisciplineEditor(Discipline? d, int planId) => ShowEditor(d == null ? "Nova disciplina" : "Editar disciplina",
        $"Edital: {_snapshot.Plans.Single(p => p.Id == planId).Name}", new EditorViewModel().Text("name", "Nome da disciplina *", d?.Name ?? ""),
        async v =>
        {
            var id = await _store.SaveDisciplineAsync(d?.Id, planId, v["name"].Text);
            _selectedNode = new ContentNode { Id = id, Kind = EntityKind.Discipline, Name = v["name"].Text };
        });
    private Task NewTopic(bool child) => TopicEditor(null, ContextDisciplineId!.Value, child ? CurrentTopic!.Id : null);
    private Task TopicEditor(Topic? t, int disciplineId, int? parent)
    {
        var isGroup = t != null && _snapshot.IsGroup(t);
        var draft = new EditorViewModel().Text("name", parent == null ? "Nome do assunto *" : "Nome do subassunto *", t?.Name ?? "")
            .Choice("status", isGroup ? "Status — alterar aplica a todos os subassuntos" : "Status", StatusChoices.Skip(1).ToArray(), (int)(t?.Status ?? TopicStatus.NotStarted))
            .Choice("priority", "Prioridade", PriorityChoices.Skip(1).ToArray(), (int)(t?.Priority ?? Priority.Medium))
            .Text("notes", "Observações (opcional)", t?.Notes ?? "", true);
        return ShowEditor(t == null ? (parent == null ? "Novo assunto" : "Novo subassunto") : "Editar conteúdo",
            "Disciplina: " + _snapshot.Disciplines.Single(d => d.Id == disciplineId).Name +
            (isGroup ? ". O status do grupo é calculado pelos filhos. Se você alterar o status neste formulário, ele será aplicado a todos os filhos." : ""), draft,
            async v =>
            {
                // Ao editar apenas nome/notas de um grupo misto, preserve os status individuais.
                var id = await _store.SaveTopicAsync(t?.Id, disciplineId, parent, v["name"].Text,
                    (TopicStatus)(v["status"].Number ?? 0), (Priority)(v["priority"].Number ?? 1), v["notes"].Text,
                    applyGroupStatus: !isGroup || v["status"].Number != (int)t!.Status);
                _selectedNode = new ContentNode { Id = id, Kind = EntityKind.Topic, Name = v["name"].Text };
            });
    }
    private Task EditContent() => SelectedNode!.Kind switch
    {
        EntityKind.Plan => PlanEditor(_snapshot.Plans.Single(p => p.Id == SelectedNode.Id)),
        EntityKind.Discipline => DisciplineEditor(_snapshot.Disciplines.Single(d => d.Id == SelectedNode.Id), ContextPlanId!.Value),
        _ => TopicEditor(CurrentTopic, CurrentTopic!.DisciplineId, CurrentTopic.ParentId)
    };
    private static bool Confirm(DeleteImpact impact) => MessageBox.Show(Application.Current.MainWindow, impact.Message,
        "Confirmar exclusão", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private async Task DeleteContent()
    {
        var node = SelectedNode!;
        if (!Confirm(_snapshot.Impact(node.Kind, node.Id))) return;
        await _store.DeleteAsync(node.Kind, node.Id); _selectedNode = null; await Reload();
    }
    private bool CanMove(int direction)
    {
        if (SelectedNode?.Kind == EntityKind.Discipline)
        {
            var d = _snapshot.Disciplines.SingleOrDefault(d => d.Id == SelectedNode.Id); if (d == null) return false;
            var siblings = _snapshot.Disciplines.Where(x => x.PlanId == d.PlanId).Select(x => x.Id).ToList();
            var target = siblings.IndexOf(d.Id) + direction; return target >= 0 && target < siblings.Count;
        }
        if (CurrentTopic is { } t)
        {
            var siblings = _snapshot.Topics.Where(x => x.DisciplineId == t.DisciplineId && x.ParentId == t.ParentId).Select(x => x.Id).ToList();
            var target = siblings.IndexOf(t.Id) + direction; return target >= 0 && target < siblings.Count;
        }
        return false;
    }
    private async Task Move(int direction) { await _store.MoveAsync(SelectedNode!.Kind, SelectedNode.Id, direction); await Reload(); }
    private int? SessionTopicId() => PageIndex switch
    {
        2 => SelectedTopicRow?.Id, 4 => SelectedReview?.TopicId, 3 => SelectedSession is { } row ? _snapshot.Sessions.Single(s => s.Id == row.Id).TopicId : null,
        _ => CurrentTopic?.Id
    };
    private Task NewSession() => SessionEditor(null, SessionTopicId(), PageIndex == 4 ? SessionKind.Review : SessionKind.Study);
    private Task EditSession()
    {
        var session = _snapshot.Sessions.Single(s => s.Id == SelectedSession!.Id);
        return SessionEditor(session, session.TopicId, session.Kind);
    }
    private Task SessionEditor(StudySession? session, int? selectedTopic, SessionKind kind)
    {
        var choices = _snapshot.Topics.OrderBy(_snapshot.PathOf).Select(t => new Choice(t.Id, _snapshot.PathOf(t))).ToArray();
        var draft = new EditorViewModel().Choice("topic", "Assunto *", choices, selectedTopic ?? choices[0].Id, session != null)
            .Choice("kind", "Tipo de sessão", [new(0, "Estudo"), new(1, "Revisão")], (int)kind, session != null)
            .Date("date", "Data *", session?.Date ?? DateOnly.FromDateTime(DateTime.Now))
            .Text("minutes", "Duração em minutos *", session?.Minutes.ToString(CultureInfo.CurrentCulture) ?? "")
            .Text("notes", "Anotação (opcional)", session?.Notes ?? "", true);
        return ShowEditor(session == null ? "Registrar sessão" : "Editar sessão",
            "A duração é informada manualmente. Estudos iniciam o primeiro ciclo; revisões avançam o ciclo ativo do assunto. O status é alterado separadamente.", draft,
            async v =>
            {
                if (!int.TryParse(v["minutes"].Text, out var minutes)) throw new InvalidOperationException("Informe a duração em minutos inteiros. Exemplo: 45.");
                await _store.SaveSessionAsync(session?.Id, v["topic"].Number ?? throw new InvalidOperationException("Selecione um assunto."),
                    (SessionKind)(v["kind"].Number ?? 0), v.GetDate("date") ?? throw new InvalidOperationException("Informe a data da sessão."), minutes, v["notes"].Text);
            });
    }
    private async Task DeleteSession()
    {
        var id = SelectedSession!.Id;
        var impact = _snapshot.Impact(EntityKind.Session, id);
        if (!Confirm(impact with { Name = impact.Name + " — o ciclo será recalculado quando aplicável" })) return;
        await _store.DeleteAsync(EntityKind.Session, id); await Reload();
    }
    private async Task RestartCycle() { await _store.StartCycleAsync(CurrentTopic!.Id); await Reload(); }
    private Task OpenTopic()
    {
        var id = PageIndex == 2 ? SelectedTopicRow!.Id : SelectedReview!.TopicId;
        var topic = _snapshot.Topics.Single(t => t.Id == id);
        _selectedNode = new ContentNode { Id = id, Kind = EntityKind.Topic, Name = topic.Name };
        PageIndex = 1; RefreshViews(); return Task.CompletedTask;
    }
    private Task Settings() => ShowEditor("Intervalos de revisão",
        "Informe intervalos em dias separados por vírgula. Cada intervalo começa após a etapa anterior. A mudança afeta somente novos ciclos.",
        new EditorViewModel().Text("intervals", "Intervalos *", string.Join(", ", _snapshot.Intervals)),
        v => _store.SaveIntervalsAsync(v["intervals"].Text));
}
