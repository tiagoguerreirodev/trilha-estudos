using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using TrilhaEstudos.App;
using TrilhaEstudos.Core;

internal static class UiChecks
{
    public static Task RunAsync(string testRoot, string outputDirectory)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/TrilhaEstudos;component/Themes/Controls.xaml", UriKind.Relative)
            });
            var trace = new StringWriter();
            var listener = new TextWriterTraceListener(trace);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            application.Dispatcher.BeginInvoke(new Action(async () =>
            {
                MainWindow? window = null;
                try
                {
                    Directory.CreateDirectory(outputDirectory);
                    var store = new StudyStore(Path.Combine(testRoot, "ui", "isolated.db"));
                    await store.InitializeAsync();
                    var allowGroup = false;
                    var confirmations = new List<string>();
                    var vm = new MainViewModel(store, confirmPreviousStudy: message => { confirmations.Add(message); return allowGroup; });
                    window = new MainWindow { DataContext = vm, ShowActivated = false, ShowInTaskbar = false, Left = -30000, Top = -30000 };
                    application.MainWindow = window; window.Show(); await vm.InitializeAsync();
                    Require(vm.Error.Length == 0 && !vm.NeedsInitialization && vm.Empty, "A primeira execução da interface falhou: " + vm.Error);
                    await Render(window, Path.Combine(outputDirectory, "01-primeira-execucao.png"));
                    var today = DateOnly.FromDateTime(DateTime.Now);
                    await Submit(vm.NewPlanCommand, vm, v => { v["name"].Text = "Plano de verificação"; v["institution"].Text = "Ambiente temporário de teste"; v["exam"].Date = today.AddDays(90).ToDateTime(TimeOnly.MinValue); });
                    var p = vm.Tree[0].Id;
                    await Submit(vm.NewDisciplineCommand, vm, v => v["name"].Text = "Linguagem");
                    var d = vm.SelectedNode!.Id;
                    await Submit(vm.NewTopicCommand, vm, v => { v["name"].Text = "Leitura e interpretação"; v["priority"].Number = 2; v["notes"].Text = "Organizar a leitura por etapas."; });
                    var group = vm.SelectedNode!.Id;
                    await store.SaveTopicAsync(null, d, group, "Contexto", TopicStatus.Completed, Priority.Medium, "");
                    await store.SaveTopicAsync(null, d, group, "Argumentação", TopicStatus.NotStarted, Priority.High, "Analisar as ideias principais.");
                    var t = await store.SaveTopicAsync(null, d, null, "Revisão de conceitos", TopicStatus.InProgress, Priority.Medium, "");
                    await vm.InitializeAsync();
                    vm.PageIndex = 1;
                    vm.SelectedNode = vm.Tree[0].Children[0].Children.Single(n => n.Id == t);
                    await Submit(vm.NewSessionCommand, vm, v => { v["date"].Date = today.AddDays(-3).ToDateTime(TimeOnly.MinValue); v["minutes"].Text = "45"; v["notes"].Text = "Anotação de verificação"; });
                    await vm.InitializeAsync();
                    Require(vm.Summary.Total == 3 && vm.Summary.Completed == 1 && vm.ReviewRows.Count == 1, "O painel não recebeu os dados.");
                    await Execute(vm.MarkPreviouslyStudiedCommand, vm);
                    Require(vm.SelectionDetails.Contains("Estudado anteriormente") && vm.Summary.Completed == 2
                        && vm.Summary.Minutes == 45 && vm.SessionRows.Count == 1 && vm.ReviewRows.Count == 1 && confirmations.Count == 0,
                        $"Marcação individual divergente: seleção={vm.SelectionTitle}, concluídos={vm.Summary.Completed}, minutos={vm.Summary.Minutes}, sessões={vm.SessionRows.Count}, revisões={vm.ReviewRows.Count}, confirmações={confirmations.Count}, detalhes={vm.SelectionDetails}.");
                    vm.PageIndex = 2; vm.Search = "ARGUMENTACAO"; vm.SelectedTopicRow = vm.TopicRows.Single();
                    await Execute(vm.MarkPreviouslyStudiedCommand, vm);
                    Require(vm.TopicRows.Single().PreviousStudy.Contains("Estudado anteriormente"), "Busca não apresentou marcação.");
                    await Execute(vm.RemovePreviouslyStudiedCommand, vm);
                    Require(vm.TopicRows.Single().Status == "Concluído" && vm.TopicRows.Single().PreviousStudy.Contains("Sem marcação"), "Remover na busca alterou status.");
                    vm.Search = ""; vm.PageIndex = 1; vm.SelectedNode = vm.Tree[0].Children[0].Children.Single(n => n.Id == group);
                    var activities = (await store.ReadAsync()).Activities.Count;
                    await Execute(vm.MarkPreviouslyStudiedCommand, vm);
                    Require(confirmations.Count == 1 && confirmations[0].Contains("2 subassunto(s)") && (await store.ReadAsync()).Activities.Count == activities, "Cancelar grupo gravou dados ou omitiu quantidade.");
                    allowGroup = true; await Execute(vm.MarkPreviouslyStudiedCommand, vm);
                    Require(vm.SelectionDetails.Contains("Todos os subassuntos") && vm.Summary.Completed == 3, "Grupo não atualizou interface.");
                    Require(vm.SelectedNode!.Detail.Contains("Estudado anteriormente"), "Árvore não apresentou marcação.");
                    for (var page = 0; page < 6; page++)
                    {
                        vm.PageIndex = page;
                        if (page == 1) vm.SelectedNode = vm.Tree[0].Children[0].Children[0];
                        await Render(window, Path.Combine(outputDirectory, $"0{page + 2}-tela-{page}.png"));
                    }
                    vm.PageIndex = 2; vm.Search = "ARGUMENTACAO";
                    Require(vm.TopicRows.Count == 1, "A busca na interface não ignorou acentos.");
                    vm.Search = ""; window.Width = 1024; window.Height = 720;
                    await Render(window, Path.Combine(outputDirectory, "08-busca-tamanho-minimo.png"));
                    vm.PageIndex = 1;
                    await Render(window, Path.Combine(outputDirectory, "09-conteudo-tamanho-minimo.png"));
                    await CheckTreeColors(window, vm, store, p, d, group, t, outputDirectory);
                    var draft = new EditorViewModel().Text("name", "Nome do assunto *", "")
                        .Choice("status", "Status", [new(0, "Não iniciado"), new(1, "Em andamento"), new(2, "Concluído")], 0)
                        .Date("date", "Data (opcional)", today).Text("notes", "Observações (opcional)", "", true);
                    var editor = new EditorDialog("Verificação de formulário", "Formulário criado somente pela verificação automatizada.", draft, _ => Task.CompletedTask)
                    { ShowActivated = false, Left = -30000, Top = -30000 };
                    editor.WindowStartupLocation = WindowStartupLocation.Manual;
                    editor.Show();
                    await Render(editor, Path.Combine(outputDirectory, "10-formulario.png"));
                    editor.Close();
                    listener.Flush();
                    Require(trace.ToString().Length == 0, "Erros de binding WPF: " + trace);
                    completion.SetResult();
                }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally
                {
                    window?.Close();
                    PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                    listener.Dispose(); application.Dispatcher.InvokeShutdown();
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "TrilhaEstudos.UiChecks" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task CheckTreeColors(Window window, MainViewModel vm, StudyStore store,
        int plan, int discipline, int group, int topic, string output)
    {
        vm.PageIndex = 1;
        async Task Refresh()
        {
            await vm.InitializeAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Require(vm.Error.Length == 0, vm.Error);
        }
        ContentNode Node(int id, EntityKind kind) => Descendants(window).OfType<Border>()
            .Where(b => b.Name == "StatusHeader").Select(b => b.DataContext).OfType<ContentNode>().First(n => n.Id == id && n.Kind == kind);
        Border Header(int id, EntityKind kind = EntityKind.Topic) => Descendants(window).OfType<Border>()
            .Single(b => b.Name == "StatusHeader" && b.DataContext is ContentNode n && n.Id == id && n.Kind == kind);
        void ColorIs(int id, EntityKind kind, string color)
            => Require(((SolidColorBrush)Header(id, kind).Background).Color == (Color)ColorConverter.ConvertFromString(color), $"Cor incorreta em {kind}/{id}.");
        void Neutral(int id, EntityKind kind)
            => Require(((SolidColorBrush)Header(id, kind).Background).Color.A == 0, $"Item {kind}/{id} deveria ser neutro.");
        await Refresh();
        ColorIs(discipline, EntityKind.Discipline, "#E6F4EA"); Neutral(plan, EntityKind.Plan);
        await store.SetPreviouslyStudiedAsync(group, false); await Refresh();
        ColorIs(discipline, EntityKind.Discipline, "#E6F4EA");
        var s = await store.ReadAsync(); var t = s.Topics.Single(x => x.Id == topic);
        await store.SaveTopicAsync(t.Id, t.DisciplineId, t.ParentId, t.Name, TopicStatus.InProgress, t.Priority, t.Notes); await Refresh();
        Require((await store.ReadAsync()).Topics.Single(x => x.Id == topic).PreviouslyStudied, "Histórico foi perdido ao reabrir assunto.");
        ColorIs(topic, EntityKind.Topic, "#E8F0FE"); Neutral(discipline, EntityKind.Discipline);
        var added = await store.SaveTopicAsync(null, discipline, group,
            "Um subassunto com nome longo para conferir a quebra de linhas e a legibilidade do status na árvore em uma janela menor",
            TopicStatus.NotStarted, Priority.Medium, "Teste isolado da interface.");
        var empty = await store.SaveDisciplineAsync(null, plan, "Disciplina vazia");
        var completed = await store.SaveDisciplineAsync(null, plan, "Disciplina totalmente estudada");
        await store.SaveTopicAsync(null, completed, null, "Conteúdo concluído", TopicStatus.Completed, Priority.Medium, "");
        await Refresh();
        ColorIs(added, EntityKind.Topic, "#FCE8E6"); ColorIs(group, EntityKind.Topic, "#E8F0FE");
        var greenChild = (await store.ReadAsync()).Topics.First(x => x.ParentId == group && x.Id != added).Id;
        ColorIs(greenChild, EntityKind.Topic, "#E6F4EA");
        Neutral(empty, EntityKind.Discipline); ColorIs(completed, EntityKind.Discipline, "#E6F4EA");
        vm.SelectedNode = Node(topic, EntityKind.Topic); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        ColorIs(topic, EntityKind.Topic, "#E8F0FE");
        Require(((SolidColorBrush)Header(topic).BorderBrush).Color == (Color)ColorConverter.ConvertFromString("#075DA8")
            && Header(topic).BorderThickness == new Thickness(2), "Seleção apagou cor ou contorno.");
        var groupContainer = Descendants(window).OfType<TreeViewItem>().Single(x => x.DataContext is ContentNode n && n.Kind == EntityKind.Topic && n.Id == group);
        groupContainer.IsExpanded = false; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Require(Descendants(groupContainer).OfType<ItemsPresenter>().First().Visibility == Visibility.Collapsed, "Recolher grupo deixou filhos visíveis.");
        groupContainer.IsExpanded = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Require(Descendants(groupContainer).OfType<ItemsPresenter>().First().Visibility == Visibility.Visible, "Expandir grupo não restaurou filhos.");
        await Execute(vm.MoveUpCommand, vm);
        ColorIs(topic, EntityKind.Topic, "#E8F0FE");
        window.Width = 1320; window.Height = 960;
        await Render(window, Path.Combine(output, "11-cores-status.png"));
        window.Width = 1024; window.Height = 720;
        await Render(window, Path.Combine(output, "12-cores-tamanho-minimo.png"));
        // Substitui somente recursos desta janela; não altera a acessibilidade do Windows.
        window.Resources[SystemParameters.HighContrastKey] = true;
        window.Resources[SystemColors.WindowBrushKey] = Brushes.Black;
        window.Resources[SystemColors.WindowTextBrushKey] = Brushes.White;
        window.Resources[SystemColors.HighlightBrushKey] = Brushes.Yellow;
        window.Resources[SystemColors.HighlightTextBrushKey] = Brushes.Black;
        await Render(window, Path.Combine(output, "13-alto-contraste-simulado.png"));
        ColorIs(topic, EntityKind.Topic, "#FFFFFF00"); ColorIs(added, EntityKind.Topic, "#FF000000");
        var container = Descendants(window).OfType<TreeViewItem>().Single(x => x.DataContext is ContentNode n && n.Kind == EntityKind.Topic && n.Id == topic);
        Require(((SolidColorBrush)container.Foreground).Color == Colors.Black, "Seleção em alto contraste ilegível.");
        foreach (var key in new object[] { SystemParameters.HighContrastKey, SystemColors.WindowBrushKey, SystemColors.WindowTextBrushKey, SystemColors.HighlightBrushKey, SystemColors.HighlightTextBrushKey }) window.Resources.Remove(key);
        await Refresh(); ColorIs(topic, EntityKind.Topic, "#E8F0FE");
        await store.SaveTopicAsync(t.Id, t.DisciplineId, t.ParentId, t.Name, TopicStatus.Completed, t.Priority, t.Notes); await Refresh();
        Neutral(discipline, EntityKind.Discipline);
        await store.DeleteAsync(EntityKind.Topic, added); await Refresh();
        ColorIs(discipline, EntityKind.Discipline, "#E6F4EA");
        await store.SaveTopicAsync(null, discipline, null, "Nova pendência", TopicStatus.NotStarted, Priority.Low, ""); await Refresh();
        Neutral(discipline, EntityKind.Discipline);
        Require(vm.Summary.Minutes == 45 && vm.SessionRows.Count == 1 && vm.ReviewRows.Count == 1, "Mudança visual alterou estudos.");
    }
    private static async Task Execute(ICommand command, MainViewModel vm)
    {
        Require(command.CanExecute(null), $"Comando de estudo anterior desabilitado: página={vm.PageIndex}, seleção={vm.SelectedNode?.Kind}/{vm.SelectedNode?.Id}, ocupado={vm.IsBusy}, erro={vm.Error}."); command.Execute(null);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (vm.IsBusy && DateTime.UtcNow < until) await Task.Delay(25);
        Require(!vm.IsBusy && vm.Error.Length == 0, "Falha no comando: " + vm.Error);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static async Task Submit(ICommand command, MainViewModel vm, Action<EditorViewModel> fill)
    {
        Require(command.CanExecute(null), "O comando de cadastro está desabilitado.");
        // O callback deve rodar dentro do loop modal aberto pelo comando logo abaixo.
        _ = Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var editor = Application.Current.Windows.OfType<EditorDialog>().Single();
            fill((EditorViewModel)editor.DataContext);
            var button = Descendants(editor).OfType<Button>().Single(b => Equals(b.Content, "Salvar"));
            button.Command.Execute(null);
        }));
        command.Execute(null);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (vm.IsBusy && DateTime.UtcNow < until) await Task.Delay(25);
        Require(!vm.IsBusy && vm.Error.Length == 0, "Falha ao cadastrar pelo formulário: " + vm.Error);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static async Task Render(Window window, string path)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
}
