using System.Windows;
using System.Windows.Input;
using TrilhaEstudos.Core;

namespace TrilhaEstudos.App;

public enum AppUpdateState { Unavailable, Idle, Checking, Current, Available, Downloading, Ready, Applying }

public sealed class UpdateViewModel : Observable
{
    private readonly IAppUpdater _updater;
    private readonly StudyStore _store;
    private readonly Func<bool> _canApply;
    private readonly Func<bool> _confirm;
    private CancellationTokenSource? _downloadToken;
    private AppRelease? _release;
    private bool _startupChecked;
    public UpdateViewModel(IAppUpdater updater, StudyStore store, Func<bool>? canApply = null, Func<bool>? confirm = null)
    {
        _updater = updater; _store = store; _canApply = canApply ?? (() => true);
        _confirm = confirm ?? (() => MessageBox.Show(Application.Current.MainWindow,
            "Reiniciar o Trilha Estudos para instalar a atualização? Um backup dos estudos será criado antes de continuar.",
            "Instalar atualização", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
        _state = updater.IsSupported ? AppUpdateState.Idle : AppUpdateState.Unavailable;
        _status = updater.IsSupported ? "Consulte se há uma versão mais recente." : updater.UnsupportedReason;
        CheckCommand = new AsyncCommand(() => CheckAsync(false), () => updater.IsSupported && !Busy && State != AppUpdateState.Ready);
        DownloadCommand = new AsyncCommand(DownloadAsync, () => State == AppUpdateState.Available);
        CancelCommand = new AsyncCommand(() => { _downloadToken?.Cancel(); return Task.CompletedTask; }, () => State == AppUpdateState.Downloading);
        ApplyCommand = new AsyncCommand(ApplyAsync, () => State == AppUpdateState.Ready && _canApply());
    }
    public string VersionText => $"Versão {AppVersion.Display}";
    public string RepositoryUrl => AppVersion.RepositoryUrl;
    private AppUpdateState _state;
    public AppUpdateState State
    {
        get => _state;
        private set { Set(ref _state, value); Raise(nameof(Busy)); Raise(nameof(IsApplying)); Raise(nameof(IsDownloading)); Raise(nameof(HasUpdate)); }
    }
    private string _status;
    public string Status { get => _status; private set => Set(ref _status, value); }
    private int _progress;
    public int Progress { get => _progress; private set => Set(ref _progress, value); }
    public bool Busy => State is AppUpdateState.Checking or AppUpdateState.Downloading or AppUpdateState.Applying;
    public bool IsApplying => State == AppUpdateState.Applying;
    public bool IsDownloading => State == AppUpdateState.Downloading;
    public bool HasUpdate => _release != null;
    public string AvailableVersion => _release == null ? "" : $"Nova versão: {_release.Version}";
    public string ReleaseNotes => _release?.Notes ?? "";
    public string BackupPath { get; private set; } = "";
    public ICommand CheckCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ApplyCommand { get; }

    public Task CheckOnStartupAsync()
    {
        if (_startupChecked) return Task.CompletedTask;
        _startupChecked = true;
        return CheckAsync(true);
    }
    public async Task CheckAsync(bool automatic)
    {
        if (!_updater.IsSupported || Busy || State == AppUpdateState.Ready) return;
        State = AppUpdateState.Checking; Status = "Consultando atualizações…";
        _release = null; RefreshRelease();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var release = await _updater.CheckAsync(timeout.Token);
            if (release != null && Version.TryParse(release.Version, out var number) && number > AppVersion.Number)
            {
                _release = release; State = AppUpdateState.Available;
                Status = "Uma nova versão está disponível. Você escolhe quando baixar e instalar.";
            }
            else { State = AppUpdateState.Current; Status = "Você está usando a versão mais recente."; }
        }
        catch (Exception)
        {
            State = AppUpdateState.Idle;
            Status = automatic ? "Não foi possível consultar as atualizações agora. Seus estudos continuam disponíveis."
                : "Não foi possível verificar as atualizações. Confira sua conexão e tente novamente.";
        }
        RefreshRelease();
    }
    public async Task DownloadAsync()
    {
        if (State != AppUpdateState.Available || _release == null) return;
        var release = _release;
        State = AppUpdateState.Downloading; Progress = 0; Status = "Baixando e validando a atualização…";
        _downloadToken = new CancellationTokenSource();
        try
        {
            await _updater.DownloadAsync(release, new Progress<int>(p => Progress = Math.Clamp(p, 0, 100)), _downloadToken.Token);
            _downloadToken.Token.ThrowIfCancellationRequested();
            Progress = 100; State = AppUpdateState.Ready;
            Status = "Download validado. Clique em Reiniciar e instalar quando estiver pronto.";
        }
        catch (OperationCanceledException) { State = AppUpdateState.Available; Progress = 0; Status = "Download cancelado. A versão atual foi mantida."; }
        catch (Exception)
        {
            State = AppUpdateState.Available; Progress = 0;
            Status = "Não foi possível baixar ou validar o pacote. A versão atual foi mantida. Tente novamente.";
        }
        finally { _downloadToken.Dispose(); _downloadToken = null; }
    }
    public async Task ApplyAsync()
    {
        if (State != AppUpdateState.Ready || _release == null || !_canApply() || !_confirm()) return;
        State = AppUpdateState.Applying; Status = "Criando backup dos estudos antes de atualizar…";
        try
        {
            BackupPath = await _store.BackupAsync(); Raise(nameof(BackupPath));
            Status = "Reiniciando para instalar a atualização…";
            _updater.ApplyAndRestart(_release);
        }
        catch (Exception)
        {
            State = AppUpdateState.Ready;
            Status = "Não foi possível preparar ou instalar a atualização. O aplicativo continua aberto e seus estudos foram preservados.";
        }
    }
    private void RefreshRelease() { Raise(nameof(HasUpdate)); Raise(nameof(AvailableVersion)); Raise(nameof(ReleaseNotes)); }
}
