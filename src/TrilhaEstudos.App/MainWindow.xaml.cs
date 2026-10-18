using System.Windows;

namespace TrilhaEstudos.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
    private void OnTreeSelection(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is ContentNode node) vm.SelectedNode = node;
    }
}
