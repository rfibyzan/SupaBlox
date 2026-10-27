using Avalonia.Controls;
using Avalonia.Input;
using SupaBlox.ViewModels;

namespace SupaBlox.Views;

public partial class MainWindow : Window
{
    public event Action? ExitRequested;

    public MainWindow()
    {
        InitializeComponent();
        PickerBox.KeyDown += OnPickerKeyDown;
        TitleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleMax();
            else BeginMoveDrag(e);
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.CopyToClipboard = async text => { if (Clipboard is { } cb) await cb.SetTextAsync(text); };
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is MainViewModel vm)
            {
                if (vm.IsPickerOpen) vm.ClosePicker();
                else if (vm.IsEditOpen) vm.IsEditOpen = false;
                else if (vm.IsAddOpen) vm.CloseAddCommand.Execute(null);
            }
        };
    }

    private void OnPickerKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Key == Key.Enter) { vm.PickFirst(); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.ClosePicker(); e.Handled = true; }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.MinimizeToTray && !e.IsProgrammatic)
        {
            e.Cancel = true;
            vm.FlushSave();
            Hide();
            return;
        }
        base.OnClosing(e);
        if (!e.Cancel) ExitRequested?.Invoke();
    }

    private void ToggleMax() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnMinimize(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ToggleMax();
    private void OnClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}
