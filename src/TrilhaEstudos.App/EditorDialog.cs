using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace TrilhaEstudos.App;

public enum FieldKind { Text, Notes, Date, Choice }
public sealed class EditorField : Observable
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public FieldKind Kind { get; init; }
    public bool ReadOnly { get; init; }
    public IReadOnlyList<Choice> Choices { get; init; } = [];
    private string _text = "";
    public string Text { get => _text; set => Set(ref _text, value); }
    private DateTime? _date;
    public DateTime? Date { get => _date; set => Set(ref _date, value); }
    private int? _number;
    public int? Number { get => _number; set => Set(ref _number, value); }
    public string Fingerprint => $"{Text}\u001f{Date:O}\u001f{Number}";
}
public sealed class EditorViewModel : Observable
{
    public List<EditorField> Fields { get; } = [];
    public EditorField this[string key] => Fields.Single(f => f.Key == key);
    private string _error = "";
    public string Error { get => _error; set => Set(ref _error, value); }
    private bool _saving;
    public bool Saving { get => _saving; set => Set(ref _saving, value); }
    public EditorViewModel Text(string key, string label, string value = "", bool notes = false)
    {
        Fields.Add(new() { Key = key, Label = label, Kind = notes ? FieldKind.Notes : FieldKind.Text, Text = value }); return this;
    }
    public EditorViewModel Date(string key, string label, DateOnly? value = null)
    {
        Fields.Add(new() { Key = key, Label = label, Kind = FieldKind.Date, Date = value?.ToDateTime(TimeOnly.MinValue) }); return this;
    }
    public EditorViewModel Choice(string key, string label, IReadOnlyList<Choice> choices, int? selected, bool readOnly = false)
    {
        Fields.Add(new() { Key = key, Label = label, Kind = FieldKind.Choice, Choices = choices, Number = selected, ReadOnly = readOnly }); return this;
    }
    public DateOnly? GetDate(string key) => this[key].Date is { } date ? DateOnly.FromDateTime(date) : null;
}

// A janela apenas apresenta campos vinculados ao rascunho; validação e gravação ficam no callback do ViewModel.
public sealed class EditorDialog : Window
{
    private readonly EditorViewModel _vm;
    private readonly string _original;
    private bool _saved;
    private string Fingerprint => string.Join('\u001e', _vm.Fields.Select(f => f.Fingerprint));
    public EditorDialog(string title, string hint, EditorViewModel vm, Func<EditorViewModel, Task> save)
    {
        _vm = vm; _original = Fingerprint;
        Title = title; Owner = Application.Current.MainWindow; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = Owner?.Icon;
        Style = (Style)FindResource(typeof(Window));
        Width = 570; Height = 680; MinWidth = 460; MinHeight = 420; ShowInTaskbar = false;
        DataContext = vm;
        var root = new DockPanel { Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        var submit = new Button { Content = "Salvar", IsDefault = true, Style = (Style)FindResource("Primary") };
        submit.Command = new AsyncCommand(async () =>
        {
            vm.Saving = true; vm.Error = "";
            try
            {
                // Força a validação dos DatePickers antes de ler o rascunho.
                foreach (var picker in _datePickers)
                {
                    var raw = picker.Text.Trim();
                    if (raw.Length == 0) picker.SelectedDate = null;
                    else if (DateTime.TryParse(raw, System.Globalization.CultureInfo.GetCultureInfo("pt-BR"),
                            System.Globalization.DateTimeStyles.None, out var parsedDate)) picker.SelectedDate = parsedDate.Date;
                    else throw new InvalidOperationException("Informe uma data válida no formato dia/mês/ano.");
                    picker.GetBindingExpression(DatePicker.SelectedDateProperty)?.UpdateSource();
                }
                await save(vm); _saved = true; DialogResult = true;
            }
            catch (Exception ex) { vm.Error = ex is InvalidOperationException ? ex.Message : "Não foi possível salvar. Verifique os campos e tente novamente."; }
            finally { vm.Saving = false; }
        }, () => !vm.Saving);
        buttons.Children.Add(cancel); buttons.Children.Add(submit); root.Children.Add(buttons);
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        error.SetBinding(TextBlock.TextProperty, new Binding(nameof(vm.Error))); DockPanel.SetDock(error, Dock.Bottom); root.Children.Add(error);
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Heading") });
        form.Children.Add(new TextBlock { Text = hint, Style = (Style)FindResource("Hint") });
        foreach (var field in vm.Fields) AddField(form, field);
        root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root; Closing += OnClosing;
        Loaded += (_, _) => MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
    private readonly List<DatePicker> _datePickers = [];
    private void AddField(Panel form, EditorField field)
    {
        form.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(0, 12, 0, 5), FontWeight = FontWeights.SemiBold });
        FrameworkElement control;
        if (field.Kind == FieldKind.Date)
        {
            var picker = new DatePicker { SelectedDateFormat = DatePickerFormat.Short, DataContext = field };
            picker.SetBinding(DatePicker.SelectedDateProperty, new Binding(nameof(field.Date)) { Mode = BindingMode.TwoWay });
            picker.DateValidationError += (_, e) => { e.ThrowException = false; _vm.Error = "Informe uma data válida no formato dia/mês/ano."; };
            _datePickers.Add(picker); control = picker;
        }
        else if (field.Kind == FieldKind.Choice)
        {
            var combo = new ComboBox { ItemsSource = field.Choices, DisplayMemberPath = "Label", SelectedValuePath = "Id", DataContext = field, IsEnabled = !field.ReadOnly };
            combo.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(field.Number)) { Mode = BindingMode.TwoWay }); control = combo;
        }
        else
        {
            var text = new TextBox { DataContext = field, AcceptsReturn = field.Kind == FieldKind.Notes,
                TextWrapping = field.Kind == FieldKind.Notes ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = field.Kind == FieldKind.Notes ? 110 : 36, MaxLength = field.Kind == FieldKind.Notes ? 10000 : 1000 };
            text.SetBinding(TextBox.TextProperty, new Binding(nameof(field.Text)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); control = text;
        }
        System.Windows.Automation.AutomationProperties.SetName(control, field.Label);
        form.Children.Add(control);
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_saved) return;
        if (_vm.Saving) { e.Cancel = true; return; }
        if (Fingerprint != _original && MessageBox.Show(this, "Descartar as alterações deste formulário?", "Alterações não salvas",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) e.Cancel = true;
    }
}
