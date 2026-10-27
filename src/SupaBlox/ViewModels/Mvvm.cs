using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SupaBlox.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void Notify(params string[] names)
    {
        foreach (var n in names) OnPropertyChanged(n);
    }
}

/// <summary>Command sync/async. Error async dikirim ke ErrorSink (ditampilkan di status bar).</summary>
public sealed class RelayCommand : ICommand
{
    public static Action<Exception>? ErrorSink;

    private readonly Func<object?, Task> _run;
    private readonly Func<object?, bool>? _can;
    private bool _busy;

    public RelayCommand(Action execute) : this(_ => { execute(); return Task.CompletedTask; }) { }
    public RelayCommand(Action<object?> execute) : this(p => { execute(p); return Task.CompletedTask; }) { }
    public RelayCommand(Func<Task> execute) : this(_ => execute()) { }

    public RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _run = execute;
        _can = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_busy && (_can?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (_busy) return;
        _busy = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await _run(parameter); }
        catch (Exception ex) { ErrorSink?.Invoke(ex); }
        finally
        {
            _busy = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
