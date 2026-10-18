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
                    var vm = new MainViewModel(store);
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
