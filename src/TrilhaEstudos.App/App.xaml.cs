using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using TrilhaEstudos.Core;
using Velopack;

namespace TrilhaEstudos.App;

public partial class App : Application
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().SetArgs(args).SetAutoApplyOnStartup(false).Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private Mutex? _mutex;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("pt-BR")));
        _mutex = new Mutex(true, "Local\\TrilhaEstudos.Desktop", out var created);
        if (!created)
        {
            MessageBox.Show("O Trilha Estudos já está aberto. Use a janela existente.", "Trilha Estudos");
            Shutdown(); return;
        }
        // A opção serve apenas para verificação com banco isolado; o uso normal utiliza LocalAppData.
        var dataIndex = Array.IndexOf(e.Args, "--data-dir");
        var path = dataIndex >= 0 && dataIndex + 1 < e.Args.Length
            ? System.IO.Path.Combine(e.Args[dataIndex + 1], "estudos.db") : StudyStore.DefaultPath;
        var store = new StudyStore(path);
        var vm = new MainViewModel(store, new VelopackAppUpdater(AppVersion.RepositoryUrl));
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        await vm.InitializeAsync();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose(); base.OnExit(e);
    }
}
