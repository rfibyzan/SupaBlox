using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using SupaBlox.ViewModels;
using SupaBlox.Views;

namespace SupaBlox;

public partial class App : Application
{
    private MainWindow? _window;
    private MainViewModel? _vm;

    public static bool UsingBuilderSans { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        UsingBuilderSans = Core.BuilderFont.TryRegister();
        Resources["AppFont"] = new Avalonia.Media.FontFamily(UsingBuilderSans
            ? "fonts:BuilderSans#Builder Sans, fonts:Inter#Inter"
            : "fonts:Inter#Inter");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _vm = new MainViewModel();
            _window = new MainWindow { DataContext = _vm };
            _window.ExitRequested += () => Exit(desktop);
            SetupTray(desktop);
            if (!Program.StartInTray || !_vm.MinimizeToTray) _window.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var show = new NativeMenuItem("Tampilkan SupaBlox");
        show.Click += (_, _) => _window?.ShowFromTray();
        var multi = new NativeMenuItem("Multi Roblox ON/OFF");
        multi.Click += (_, _) =>
        {
            if (_vm == null) return;
            _vm.MultiEnabled = !_vm.MultiEnabled;
            if (_vm.IsDisableOpen) _window?.ShowFromTray(); // dialog konfirmasi perlu jendela terlihat
        };
        var exit = new NativeMenuItem("Keluar");
        exit.Click += (_, _) => Exit(desktop);

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SupaBlox/Assets/supablox.ico"))),
            ToolTipText = "SupaBlox",
            Menu = new NativeMenu { show, multi, new NativeMenuItemSeparator(), exit },
            IsVisible = true,
        };
        tray.Clicked += (_, _) => _window?.ShowFromTray();
        TrayIcon.SetIcons(this, new TrayIcons { tray });
    }

    private bool _exiting;

    private void Exit(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // desktop.Shutdown() menutup MainWindow -> OnClosing -> ExitRequested -> Exit lagi.
        // Tanpa guard ini terjadi rekursi tak berujung (stack overflow saat aplikasi ditutup).
        if (_exiting) return;
        _exiting = true;
        _vm?.Shutdown();
        if (TrayIcon.GetIcons(this) is { } icons) foreach (var t in icons) t.IsVisible = false;
        desktop.Shutdown();
    }
}
