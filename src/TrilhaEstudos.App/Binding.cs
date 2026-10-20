using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace TrilhaEstudos.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); CommandManager.InvalidateRequerySuggested(); return true;
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true; CommandManager.InvalidateRequerySuggested();
        try { await execute(); }
        finally { _running = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
public sealed record Choice(int Id, string Label);

public sealed class ContentNode : Observable
{
    public required int Id { get; init; }
    public required Core.EntityKind Kind { get; init; }
    public required string Name { get; init; }
    public string Detail { get; init; } = "";
    public Core.TopicStatus? Status { get; init; }
    public List<ContentNode> Children { get; init; } = [];
    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}
public sealed record TopicRow(int Id, string Name, string Path, string Status, string Priority, string Time, string Group, string PreviousStudy);
public sealed record SessionRow(int Id, string Date, string Kind, string Path, string Duration, string Notes);
public sealed record ReviewRow(int TopicId, string Date, string Path, string Step, string State);
