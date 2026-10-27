using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SupaBlox.ViewModels;

namespace SupaBlox.Views;

public partial class MainWindow : Window
{
    public event Action? ExitRequested;

    private const string DragFormat = "supablox/account-id";
    private AccountItem? _dragCandidate;
    private Point _dragStart;
    private bool _dragging;

    public MainWindow()
    {
        InitializeComponent();
        PickerBox.KeyDown += OnPickerKeyDown;
        PaletteBox.KeyDown += OnPaletteKeyDown;
        FollowBox.KeyDown += (_, e) => { if (e.Key == Key.Enter && DataContext is MainViewModel vm) { vm.FollowCommand.Execute(null); e.Handled = true; } };
        NewGroupBox.KeyDown += (_, e) =>
        {
            if (DataContext is not MainViewModel vm) return;
            if (e.Key == Key.Enter) { vm.CreateGroup(); e.Handled = true; }
            else if (e.Key == Key.Escape) { vm.IsNewGroupOpen = false; e.Handled = true; }
        };
        PaletteOverlay.PointerPressed += (_, e) =>
        {
            if (e.Source == PaletteOverlay && DataContext is MainViewModel vm) vm.ClosePalette();
        };
        TitleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleMax();
            else BeginMoveDrag(e);
        };

        // Ctrl+K dari mana saja (tunnel, supaya TextBox tidak menelan tombolnya).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control) && DataContext is MainViewModel vm)
            {
                if (vm.IsPaletteOpen) vm.ClosePalette(); else vm.OpenPalette();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            vm.CopyToClipboard = async text => { if (Clipboard is { } cb) await cb.SetTextAsync(text); };
            vm.PickSaveFile = PickSaveAsync;
            vm.PickOpenFile = PickOpenAsync;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsPaletteOpen) && vm.IsPaletteOpen)
                    Dispatcher.UIThread.Post(() => { PaletteBox.Focus(); PaletteBox.CaretIndex = PaletteBox.Text?.Length ?? 0; }, DispatcherPriority.Input);
                if (e.PropertyName == nameof(MainViewModel.IsNewGroupOpen) && vm.IsNewGroupOpen)
                    Dispatcher.UIThread.Post(() => NewGroupBox.Focus(), DispatcherPriority.Input);
            };
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is MainViewModel vm)
            {
                if (vm.IsPaletteOpen) vm.ClosePalette();
                else if (vm.IsPickerOpen) vm.ClosePicker();
                else if (vm.IsTransferOpen) vm.CloseTransferCommand.Execute(null);
                else if (vm.IsDisableOpen) vm.CancelDisableCommand.Execute(null);
                else if (vm.IsEditOpen) vm.IsEditOpen = false;
                else if (vm.IsAddOpen) vm.CloseAddCommand.Execute(null);
            }
        };

        // ---- Drag kartu akun ke chip grup ----
        AccountsList.AddHandler(PointerPressedEvent, OnCardPointerPressed, RoutingStrategies.Bubble);
        AccountsList.AddHandler(PointerMovedEvent, OnCardPointerMoved, RoutingStrategies.Bubble);
        AccountsList.AddHandler(PointerReleasedEvent, (_, _) => _dragCandidate = null, RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => (DataContext as MainViewModel)?.SetDropTarget(null));
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // ---------- Search global ----------

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        switch (e.Key)
        {
            case Key.Down: vm.MovePalette(1); e.Handled = true; break;
            case Key.Up: vm.MovePalette(-1); e.Handled = true; break;
            case Key.Enter: vm.RunPalette(); e.Handled = true; break;
            case Key.Escape: vm.ClosePalette(); e.Handled = true; break;
        }
    }

    private void OnPaletteItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CommandItem item } && DataContext is MainViewModel vm) vm.RunPalette(item);
    }

    private void OnPickerKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Key == Key.Enter) { vm.PickFirst(); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.ClosePicker(); e.Handled = true; }
    }

    // ---------- Drag & drop ----------

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragCandidate = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Jangan mulai drag dari tombol / checkbox / input di dalam kartu.
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is not null) return;
        if (e.Source is Visual v2 && v2.FindAncestorOfType<CheckBox>(true) is not null) return;
        if ((e.Source as Control)?.DataContext is AccountItem acc)
        {
            _dragCandidate = acc;
            _dragStart = e.GetPosition(this);
        }
    }

    private async void OnCardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is not { } acc || _dragging) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _dragCandidate = null; return; }
        var d = e.GetPosition(this) - _dragStart;
        if (Math.Abs(d.X) < 8 && Math.Abs(d.Y) < 8) return;
        _dragging = true;
        try
        {
            var data = new DataObject();
            data.Set(DragFormat, acc.Model.Id);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        finally
        {
            _dragging = false;
            _dragCandidate = null;
            (DataContext as MainViewModel)?.SetDropTarget(null);
        }
    }

    private static GroupChip? ChipFrom(object? source) =>
        source is Visual v ? (v.FindAncestorOfType<Button>(true)?.DataContext as GroupChip) : null;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var chip = ChipFrom(e.Source);
        bool ok = e.Data.Contains(DragFormat) && chip is { AcceptsDrop: true };
        e.DragEffects = ok ? DragDropEffects.Move : DragDropEffects.None;
        (DataContext as MainViewModel)?.SetDropTarget(ok ? chip : null);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.SetDropTarget(null);
        var chip = ChipFrom(e.Source);
        if (chip?.Key is not { } key || e.Data.Get(DragFormat) is not string id) return;
        vm.MoveAccountsToGroup(id, key);
    }

    // ---------- File picker ----------

    private async Task<string?> PickSaveAsync(string suggested)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Simpan ekspor akun",
            SuggestedFileName = suggested,
            DefaultExtension = "sbx",
            FileTypeChoices = new[] { new FilePickerFileType("Ekspor SupaBlox") { Patterns = new[] { "*.sbx" } } },
        });
        return file?.TryGetLocalPath();
    }

    private async Task<string?> PickOpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih file ekspor SupaBlox",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Ekspor SupaBlox") { Patterns = new[] { "*.sbx" } } },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    // ---------- Window ----------

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

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object? sender, RoutedEventArgs e) => ToggleMax();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}
