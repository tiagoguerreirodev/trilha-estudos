using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TrilhaEstudos.Core;

public sealed class StudyStore
{
    private readonly string _path;
    private readonly Func<DateOnly> _today;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string DatabasePath => _path;
    public StudyStore(string path, Func<DateOnly>? today = null)
    {
        _path = Path.GetFullPath(path);
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
    }
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "TrilhaEstudos", "estudos.db");

    private async Task<T> Run<T>(Func<SqliteConnection, T> action)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                    {
                        DataSource = _path, ForeignKeys = true, Pooling = false, DefaultTimeout = 5
                    }.ToString());
                    connection.Open();
                    return action(connection);
                }
                catch (SqliteException ex)
                {
                    throw new InvalidOperationException("Não foi possível acessar ou salvar o banco local. " +
                        "Verifique se a pasta está disponível, se há espaço em disco e tente novamente. Seus dados não foram reinicializados.", ex);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException("Não foi possível acessar a pasta dos dados. Verifique as permissões e o espaço em disco.", ex);
                }
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static SqliteCommand Command(SqliteConnection c, SqliteTransaction? tx, string sql,
        params (string Key, object? Value)[] args)
    {
        var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
    private static int Exec(SqliteConnection c, SqliteTransaction? tx, string sql,
        params (string Key, object? Value)[] args)
    {
        using var cmd = Command(c, tx, sql, args); return cmd.ExecuteNonQuery();
    }
    private static int Scalar(SqliteConnection c, SqliteTransaction tx, string sql,
        params (string Key, object? Value)[] args)
    {
        using var cmd = Command(c, tx, sql, args); return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Name(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 200) throw new InvalidOperationException("Informe um nome entre 1 e 200 caracteres.");
        return name;
    }
    private static string Notes(string text)
    {
        if (text.Length > 10000) throw new InvalidOperationException("As observações devem ter até 10.000 caracteres.");
        return text.Trim();
    }
    public static int[] ParseIntervals(string value)
    {
        var pieces = value.Split(',', StringSplitOptions.TrimEntries);
        if (pieces.Length is < 1 or > 12 || pieces.Any(p => !int.TryParse(p, out var n) || n is < 1 or > 3650))
            throw new InvalidOperationException("Informe de 1 a 12 intervalos de 1 a 3650 dias, separados por vírgula. Exemplo: 1, 7, 30.");
        return pieces.Select(int.Parse).ToArray();
    }

    public Task InitializeAsync() => Run(c =>
    {
        using var version = c.CreateCommand(); version.CommandText = "PRAGMA user_version";
        var schema = Convert.ToInt32(version.ExecuteScalar());
        if (schema > 2) throw new InvalidOperationException("Este banco foi criado por uma versão mais recente do aplicativo. Use a versão correspondente; o banco foi preservado.");
        Exec(c, null, "PRAGMA journal_mode=WAL");
        if (schema == 1)
        {
            try { Backup(c); }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                throw new InvalidOperationException("Não foi possível criar e validar o backup antes de atualizar o banco. A migração não foi iniciada e seus dados foram preservados. Verifique as permissões e o espaço em disco.", ex);
            }
        }
        using var tx = c.BeginTransaction();
        if (schema == 0)
        {
            Exec(c, tx, """
                CREATE TABLE StudyPlan(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Institution TEXT NOT NULL, ExamDate TEXT, Notes TEXT NOT NULL);
                CREATE TABLE Discipline(Id INTEGER PRIMARY KEY, PlanId INTEGER NOT NULL REFERENCES StudyPlan(Id) ON DELETE CASCADE, Name TEXT NOT NULL, Position INTEGER NOT NULL);
                CREATE TABLE Topic(Id INTEGER PRIMARY KEY, DisciplineId INTEGER NOT NULL REFERENCES Discipline(Id) ON DELETE CASCADE,
                    ParentId INTEGER REFERENCES Topic(Id) ON DELETE CASCADE, Name TEXT NOT NULL,
                    Status INTEGER NOT NULL CHECK(Status BETWEEN 0 AND 2), Priority INTEGER NOT NULL CHECK(Priority BETWEEN 0 AND 2),
                    Notes TEXT NOT NULL, Position INTEGER NOT NULL,
                    PreviouslyStudied INTEGER NOT NULL DEFAULT 0 CHECK(PreviouslyStudied IN (0,1)), CHECK(ParentId IS NULL OR ParentId <> Id));
                CREATE TABLE ReviewCycle(Id INTEGER PRIMARY KEY, TopicId INTEGER NOT NULL REFERENCES Topic(Id) ON DELETE CASCADE,
                    BaseDate TEXT NOT NULL, AnchorSessionId INTEGER REFERENCES StudySession(Id) ON DELETE SET NULL,
                    Intervals TEXT NOT NULL, Superseded INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE StudySession(Id INTEGER PRIMARY KEY, TopicId INTEGER NOT NULL REFERENCES Topic(Id) ON DELETE CASCADE,
                    Kind INTEGER NOT NULL CHECK(Kind BETWEEN 0 AND 1), Date TEXT NOT NULL,
                    Minutes INTEGER NOT NULL CHECK(Minutes BETWEEN 1 AND 1440), Notes TEXT NOT NULL,
                    CycleId INTEGER REFERENCES ReviewCycle(Id) ON DELETE SET NULL);
                CREATE TABLE Setting(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                INSERT INTO Setting VALUES('ReviewIntervals','1,7,30');
                CREATE TABLE Activity(Id INTEGER PRIMARY KEY, PlanId INTEGER REFERENCES StudyPlan(Id) ON DELETE SET NULL,
                    Timestamp TEXT NOT NULL, Description TEXT NOT NULL);
                CREATE INDEX IX_Discipline_Plan ON Discipline(PlanId, Position);
                CREATE INDEX IX_Topic_Discipline ON Topic(DisciplineId, ParentId, Position);
                CREATE INDEX IX_Session_Topic ON StudySession(TopicId, Date);
                CREATE INDEX IX_Session_Cycle ON StudySession(CycleId);
                CREATE INDEX IX_Cycle_Topic ON ReviewCycle(TopicId);
                PRAGMA user_version=2;
                """);
        }
        else if (schema == 1)
        {
            Exec(c, tx, "ALTER TABLE Topic ADD COLUMN PreviouslyStudied INTEGER NOT NULL DEFAULT 0 CHECK(PreviouslyStudied IN (0,1)); PRAGMA user_version=2;");
        }
        tx.Commit(); return true;
    });

    public Task<StudySnapshot> ReadAsync() => Run(c =>
    {
        using var tx = c.BeginTransaction(); var snapshot = Read(c, tx); tx.Commit(); return snapshot;
    });

    public Task<string> BackupAsync() => Run(Backup);
    private string Backup(SqliteConnection c)
    {
        var folder = Path.Combine(Path.GetDirectoryName(_path)!, "backups");
        Directory.CreateDirectory(folder);
        var backupPath = Path.Combine(folder, $"{Guid.NewGuid():N}.db");
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = backupPath, Pooling = false }.ToString());
        backup.Open();
        c.BackupDatabase(backup);
        using var check = backup.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Não foi possível validar o backup do banco local.");
        return backupPath;
    }
    private static StudySnapshot Read(SqliteConnection c, SqliteTransaction tx)
    {
        List<T> Rows<T>(string sql, Func<SqliteDataReader, T> map)
        {
            using var cmd = Command(c, tx, sql); using var reader = cmd.ExecuteReader();
            var rows = new List<T>(); while (reader.Read()) rows.Add(map(reader)); return rows;
        }
        int? NullableId(SqliteDataReader r, int index) => r.IsDBNull(index) ? null : r.GetInt32(index);
        return new StudySnapshot
        {
            Plans = Rows("SELECT * FROM StudyPlan ORDER BY Name, Id", r => new StudyPlan(r.GetInt32(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : Date(r.GetString(3)), r.GetString(4))),
            Disciplines = Rows("SELECT * FROM Discipline ORDER BY Position, Id", r => new Discipline(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetInt32(3))),
            Topics = Rows("SELECT Id,DisciplineId,ParentId,Name,Status,Priority,Notes,Position,PreviouslyStudied FROM Topic ORDER BY Position, Id", r => new Topic(r.GetInt32(0), r.GetInt32(1), NullableId(r, 2), r.GetString(3), (TopicStatus)r.GetInt32(4), (Priority)r.GetInt32(5), r.GetString(6), r.GetInt32(7), r.GetBoolean(8))),
            Sessions = Rows("SELECT * FROM StudySession ORDER BY Date DESC, Id DESC", r => new StudySession(r.GetInt32(0), r.GetInt32(1), (SessionKind)r.GetInt32(2), Date(r.GetString(3)), r.GetInt32(4), r.GetString(5), NullableId(r, 6))),
            Cycles = Rows("SELECT * FROM ReviewCycle ORDER BY Id", r => new ReviewCycle(r.GetInt32(0), r.GetInt32(1), Date(r.GetString(2)), NullableId(r, 3), ParseIntervals(r.GetString(4)), r.GetBoolean(5))),
            Activities = Rows("SELECT * FROM Activity ORDER BY Id DESC", r => new Activity(r.GetInt32(0), NullableId(r, 1), DateTimeOffset.Parse(r.GetString(2), CultureInfo.InvariantCulture), r.GetString(3))),
            Intervals = Rows("SELECT Value FROM Setting WHERE Key='ReviewIntervals'", r => ParseIntervals(r.GetString(0))).Single()
        };
    }

    private Task<T> Mutate<T>(Func<SqliteConnection, SqliteTransaction, StudySnapshot, T> change) => Run(c =>
    {
        using var tx = c.BeginTransaction();
        Normalize(c, tx);
        var result = change(c, tx, Read(c, tx));
        Normalize(c, tx);
        tx.Commit(); return result;
    });
    private static void Normalize(SqliteConnection c, SqliteTransaction tx) => Exec(c, tx, """
        UPDATE Topic SET Status = CASE
            WHEN NOT EXISTS(SELECT 1 FROM Topic child WHERE child.ParentId=Topic.Id AND child.Status<>2) THEN 2
            WHEN NOT EXISTS(SELECT 1 FROM Topic child WHERE child.ParentId=Topic.Id AND child.Status<>0) THEN 0
            ELSE 1 END
        WHERE EXISTS(SELECT 1 FROM Topic child WHERE child.ParentId=Topic.Id);
        """);
    private static void Log(SqliteConnection c, SqliteTransaction tx, int? plan, string description) =>
        Exec(c, tx, "INSERT INTO Activity(PlanId,Timestamp,Description) VALUES($p,$t,$d)",
            ("$p", plan), ("$t", DateTimeOffset.UtcNow.ToString("O")), ("$d", description));
    private static int InsertId(SqliteConnection c, SqliteTransaction tx) => Scalar(c, tx, "SELECT last_insert_rowid()");

    public Task<int> SavePlanAsync(int? id, string name, string institution, DateOnly? exam, string notes) => Mutate((c, tx, s) =>
    {
        name = Name(name); institution = institution.Trim(); notes = Notes(notes);
        if (institution.Length > 200) throw new InvalidOperationException("A instituição/prova deve ter até 200 caracteres.");
        if (id != null && !s.Plans.Any(p => p.Id == id)) throw new InvalidOperationException("O edital não foi encontrado.");
        var args = new (string, object?)[] { ("$id", id), ("$n", name), ("$i", institution), ("$e", exam is { } date ? Iso(date) : null), ("$o", notes) };
        Exec(c, tx, id == null
            ? "INSERT INTO StudyPlan(Name,Institution,ExamDate,Notes) VALUES($n,$i,$e,$o)"
            : "UPDATE StudyPlan SET Name=$n,Institution=$i,ExamDate=$e,Notes=$o WHERE Id=$id", args);
        var result = id ?? InsertId(c, tx);
        Log(c, tx, result, $"Edital {(id == null ? "criado" : "editado")}: {name}"); return result;
    });
    public Task<int> SaveDisciplineAsync(int? id, int planId, string name) => Mutate((c, tx, s) =>
    {
        name = Name(name);
        if (!s.Plans.Any(p => p.Id == planId)) throw new InvalidOperationException("Selecione um edital válido.");
        if (id != null && !s.Disciplines.Any(d => d.Id == id && d.PlanId == planId)) throw new InvalidOperationException("A disciplina não foi encontrada neste edital.");
        var position = s.Disciplines.Where(d => d.PlanId == planId).Select(d => d.Position).DefaultIfEmpty(-1).Max() + 1;
        Exec(c, tx, id == null ? "INSERT INTO Discipline(PlanId,Name,Position) VALUES($p,$n,$o)"
            : "UPDATE Discipline SET Name=$n WHERE Id=$id", ("$p", planId), ("$n", name), ("$o", position), ("$id", id));
        var result = id ?? InsertId(c, tx);
        Log(c, tx, planId, $"Disciplina {(id == null ? "criada" : "editada")}: {name}"); return result;
    });
    public Task<int> SaveTopicAsync(int? id, int disciplineId, int? parentId, string name,
        TopicStatus status, Priority priority, string notes, bool applyGroupStatus = true) => Mutate((c, tx, s) =>
    {
        name = Name(name); notes = Notes(notes);
        if (!Enum.IsDefined(status) || !Enum.IsDefined(priority)) throw new InvalidOperationException("Status ou prioridade inválidos.");
        var discipline = s.Disciplines.SingleOrDefault(d => d.Id == disciplineId)
            ?? throw new InvalidOperationException("Selecione uma disciplina válida.");
        if (parentId is int parent && !s.Topics.Any(t => t.Id == parent && t.ParentId == null && t.DisciplineId == disciplineId))
            throw new InvalidOperationException("O assunto pai deve pertencer à mesma disciplina e não pode ser um subassunto.");
        if (id != null && !s.Topics.Any(t => t.Id == id && t.ParentId == parentId && t.DisciplineId == disciplineId))
            throw new InvalidOperationException("O assunto não foi encontrado. Não é permitido mudar seu vínculo.");
        var position = s.Topics.Where(t => t.DisciplineId == disciplineId && t.ParentId == parentId).Select(t => t.Position).DefaultIfEmpty(-1).Max() + 1;
        Exec(c, tx, id == null ? "INSERT INTO Topic(DisciplineId,ParentId,Name,Status,Priority,Notes,Position) VALUES($d,$p,$n,$s,$r,$o,$i)"
            : "UPDATE Topic SET Name=$n,Status=$s,Priority=$r,Notes=$o WHERE Id=$id",
            ("$d", disciplineId), ("$p", parentId), ("$n", name), ("$s", (int)status), ("$r", (int)priority), ("$o", notes), ("$i", position), ("$id", id));
        var result = id ?? InsertId(c, tx);
        if (id != null && applyGroupStatus) Exec(c, tx, "UPDATE Topic SET Status=$s WHERE ParentId=$id", ("$s", (int)status), ("$id", id));
        Log(c, tx, discipline.PlanId, $"Assunto {(id == null ? "criado" : "editado")}: {name}"); return result;
    });

    public Task SetPreviouslyStudiedAsync(int topicId, bool value) => Mutate((c, tx, s) =>
    {
        var topic = s.Topics.SingleOrDefault(t => t.Id == topicId)
            ?? throw new InvalidOperationException("Selecione um assunto válido.");
        var family = s.Topics.Where(t => t.Id == topicId || t.ParentId == topicId).ToArray();
        if (family.All(t => t.PreviouslyStudied == value && (!value || t.Status == TopicStatus.Completed))) return false;
        Exec(c, tx, value
            ? "UPDATE Topic SET PreviouslyStudied=1,Status=2 WHERE Id=$id OR ParentId=$id"
            : "UPDATE Topic SET PreviouslyStudied=0 WHERE Id=$id OR ParentId=$id", ("$id", topicId));
        Log(c, tx, s.PlanIdOf(topic), value ? $"Marcado como estudado anteriormente: {topic.Name}"
            : $"Marcação de estudo anterior removida: {topic.Name}");
        return true;
    });

    public Task SetDisciplinePreviouslyStudiedAsync(int disciplineId, bool value) => Mutate((c, tx, s) =>
    {
        var discipline = s.Disciplines.SingleOrDefault(d => d.Id == disciplineId)
            ?? throw new InvalidOperationException("Selecione uma disciplina válida.");
        var topics = s.Topics.Where(t => t.DisciplineId == disciplineId).ToArray();
        if (topics.All(t => t.PreviouslyStudied == value && (!value || t.Status == TopicStatus.Completed))) return false;
        Exec(c, tx, value
            ? "UPDATE Topic SET PreviouslyStudied=1,Status=2 WHERE DisciplineId=$id"
            : "UPDATE Topic SET PreviouslyStudied=0 WHERE DisciplineId=$id", ("$id", disciplineId));
        Log(c, tx, discipline.PlanId, value ? $"Disciplina marcada como estudada anteriormente: {discipline.Name}"
            : $"Marcação de estudo anterior removida da disciplina: {discipline.Name}");
        return true;
    });

    public Task<int> SaveSessionAsync(int? id, int topicId, SessionKind kind, DateOnly date, int minutes, string notes) => Mutate((c, tx, s) =>
    {
        notes = Notes(notes);
        if (!Enum.IsDefined(kind)) throw new InvalidOperationException("Selecione o tipo de sessão.");
        if (minutes is < 1 or > 1440) throw new InvalidOperationException("A duração deve ser um número inteiro entre 1 e 1440 minutos.");
        if (date > _today()) throw new InvalidOperationException("A data da sessão não pode estar no futuro.");
        var topic = s.Topics.SingleOrDefault(t => t.Id == topicId) ?? throw new InvalidOperationException("Selecione um assunto válido.");
        var existing = id == null ? null : s.Sessions.SingleOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("A sessão não foi encontrada.");
        if (existing != null && (existing.TopicId != topicId || existing.Kind != kind))
            throw new InvalidOperationException("O assunto e o tipo de uma sessão existente não podem ser alterados. Exclua e cadastre novamente se necessário.");
        var due = id == null && kind == SessionKind.Review ? s.PendingReviews().SingleOrDefault(r => r.TopicId == topicId) : null;
        var cycleId = existing?.CycleId ?? due?.CycleId;
        if (cycleId is int cycle)
        {
            var baseDate = s.Cycles.Single(r => r.Id == cycle).BaseDate;
            if (date < baseDate) throw new InvalidOperationException("A revisão não pode anteceder o início do seu ciclo.");
        }
        var anchored = id == null ? null : s.Cycles.SingleOrDefault(r => r.AnchorSessionId == id);
        if (anchored != null && s.Sessions.Any(x => x.CycleId == anchored.Id && x.Date < date))
            throw new InvalidOperationException("A sessão inicial não pode ficar depois de uma revisão já registrada neste ciclo.");
        Exec(c, tx, id == null ? "INSERT INTO StudySession(TopicId,Kind,Date,Minutes,Notes,CycleId) VALUES($t,$k,$d,$m,$n,$c)"
            : "UPDATE StudySession SET Date=$d,Minutes=$m,Notes=$n WHERE Id=$id",
            ("$t", topicId), ("$k", (int)kind), ("$d", Iso(date)), ("$m", minutes), ("$n", notes), ("$c", cycleId), ("$id", id));
        var result = id ?? InsertId(c, tx);
        if (anchored != null) Exec(c, tx, "UPDATE ReviewCycle SET BaseDate=$d WHERE Id=$id", ("$d", Iso(date)), ("$id", anchored.Id));
        if (id == null && kind == SessionKind.Study && !s.Cycles.Any(r => r.TopicId == topicId))
            Exec(c, tx, "INSERT INTO ReviewCycle(TopicId,BaseDate,AnchorSessionId,Intervals) VALUES($t,$d,$a,$i)",
                ("$t", topicId), ("$d", Iso(date)), ("$a", result), ("$i", string.Join(',', s.Intervals)));
        Log(c, tx, s.PlanIdOf(topic), $"Sessão de {kind.Text().ToLowerInvariant()} {(id == null ? "registrada" : "editada")}: {topic.Name} ({minutes} min)");
        return result;
    });

    public Task StartCycleAsync(int topicId) => Mutate((c, tx, s) =>
    {
        var topic = s.Topics.SingleOrDefault(t => t.Id == topicId) ?? throw new InvalidOperationException("Selecione um assunto válido.");
        if (s.PendingReviews().Any(r => r.TopicId == topicId)) throw new InvalidOperationException("Conclua o ciclo atual antes de iniciar outro.");
        Exec(c, tx, "UPDATE ReviewCycle SET Superseded=1 WHERE TopicId=$t", ("$t", topicId));
        Exec(c, tx, "INSERT INTO ReviewCycle(TopicId,BaseDate,Intervals) VALUES($t,$d,$i)",
            ("$t", topicId), ("$d", Iso(_today())), ("$i", string.Join(',', s.Intervals)));
        Log(c, tx, s.PlanIdOf(topic), $"Novo ciclo de revisões: {topic.Name}"); return true;
    });
    public Task SaveIntervalsAsync(string intervals) => Mutate((c, tx, s) =>
    {
        var parsed = ParseIntervals(intervals);
        Exec(c, tx, "UPDATE Setting SET Value=$v WHERE Key='ReviewIntervals'", ("$v", string.Join(',', parsed)));
        Log(c, tx, null, "Intervalos de novos ciclos atualizados"); return true;
    });
    public Task DeleteAsync(EntityKind kind, int id) => Mutate((c, tx, s) =>
    {
        var impact = s.Impact(kind, id);
        var planId = kind switch
        {
            EntityKind.Plan => (int?)null,
            EntityKind.Discipline => s.Disciplines.Single(d => d.Id == id).PlanId,
            EntityKind.Topic => s.PlanIdOf(s.Topics.Single(t => t.Id == id)),
            _ => s.PlanIdOf(s.Topics.Single(t => t.Id == s.Sessions.Single(x => x.Id == id).TopicId))
        };
        var table = kind switch { EntityKind.Plan => "StudyPlan", EntityKind.Discipline => "Discipline", EntityKind.Topic => "Topic", EntityKind.Session => "StudySession", _ => throw new InvalidOperationException("Tipo de exclusão inválido.") };
        Exec(c, tx, $"DELETE FROM {table} WHERE Id=$id", ("$id", id));
        Log(c, tx, planId, $"Removido: {impact.Name}"); return true;
    });
    public Task MoveAsync(EntityKind kind, int id, int direction) => Mutate((c, tx, s) =>
    {
        if (direction is not (-1 or 1)) throw new InvalidOperationException("Direção inválida.");
        List<(int Id, int Position)> siblings;
        string table; int planId;
        if (kind == EntityKind.Discipline)
        {
            var item = s.Disciplines.Single(d => d.Id == id); table = "Discipline"; planId = item.PlanId;
            siblings = s.Disciplines.Where(d => d.PlanId == item.PlanId).Select(d => (d.Id, d.Position)).ToList();
        }
        else if (kind == EntityKind.Topic)
        {
            var item = s.Topics.Single(t => t.Id == id); table = "Topic"; planId = s.PlanIdOf(item);
            siblings = s.Topics.Where(t => t.DisciplineId == item.DisciplineId && t.ParentId == item.ParentId).Select(t => (t.Id, t.Position)).ToList();
        }
        else throw new InvalidOperationException("Somente disciplinas e assuntos podem ser reordenados.");
        var index = siblings.FindIndex(x => x.Id == id); var target = index + direction;
        if (target < 0 || target >= siblings.Count) return false;
        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        for (var i = 0; i < siblings.Count; i++) Exec(c, tx, $"UPDATE {table} SET Position=$p WHERE Id=$id", ("$p", i), ("$id", siblings[i].Id));
        Log(c, tx, planId, "Ordem do conteúdo atualizada"); return true;
    });
}
