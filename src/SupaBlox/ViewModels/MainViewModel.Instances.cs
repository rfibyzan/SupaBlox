using System.Collections.ObjectModel;
using System.Diagnostics;
using SupaBlox.Core;

namespace SupaBlox.ViewModels;

public sealed partial class MainViewModel
{
    // ---------- Multi Roblox ----------

    private SingletonHolder? _holder;
    private bool _multiEnabled;
    private DateTime? _multiSince;

    public bool MultiEnabled
    {
        get => _multiEnabled;
        set
        {
            if (value == _multiEnabled) return;
            if (value) EnableMulti(); else DisableMulti();
            Notify(nameof(MultiEnabled), nameof(MultiBadge), nameof(MultiBadgeFg), nameof(MultiBadgeBg), nameof(MutexText), nameof(EventText), nameof(MutexOk), nameof(EventOk), nameof(MultiShort));
        }
    }

    private void EnableMulti()
    {
        if (!OperatingSystem.IsWindows()) { ShowMessage("Multi Roblox hanya untuk Windows.", true); return; }
        int running = Launcher.RunningPids().Count;
        var h = new SingletonHolder();
        if (!h.Start(TimeSpan.FromSeconds(5)))
        {
            ShowMessage("Gagal: " + (h.Error ?? "objek singleton tidak bisa dipegang."), true);
            h.Dispose();
            return;
        }
        _holder = h;
        _multiEnabled = true;
        _multiSince = DateTime.Now;
        if (!h.MutexOwned)
            ShowMessage(running > 0
                ? "Roblox sudah jalan sebelum SupaBlox, mutex milik Roblox. Tutup semua Roblox, lalu matikan-nyalakan Multi Roblox."
                : "Mutex tidak dimiliki SupaBlox. Tutup semua Roblox lalu nyalakan ulang.", true);
    }

    private void DisableMulti()
    {
        _holder?.Dispose();
        _holder = null;
        _multiEnabled = false;
        _multiSince = null;
        UptimeText = "—";
        UptimeSub = "Multi Roblox mati";
    }

    public bool MutexOk => _holder?.MutexOwned == true;
    public bool EventOk => _holder?.EventHeld == true;
    public Avalonia.Media.IBrush MultiBadgeFg => !_multiEnabled ? Palette.Dim : MutexOk ? Palette.Green : Palette.Yellow;
    public Avalonia.Media.IBrush MultiBadgeBg => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(!_multiEnabled ? "#1B2237" : MutexOk ? "#14302C" : "#3A2E12"));
    public string MultiBadge => !_multiEnabled ? "MATI" : MutexOk ? "AKTIF" : "SEBAGIAN";
    public string MultiShort => !_multiEnabled ? "Mati" : MutexOk ? $"Mutex dipegang • {InstanceCount} instance" : "Mutex milik Roblox, cek tab Instance";
    public string MutexText => "ROBLOX_singletonMutex • " + (_holder == null ? "dilepas" : _holder.MutexOwned ? "dipegang" : _holder.MutexExisted ? "milik proses lain" : "gagal");
    public string EventText => "ROBLOX_singletonEvent • " + (_holder == null ? "dilepas" : _holder.EventHeld ? "dipegang" : "gagal");

    private string _uptime = "—", _uptimeSub = "Multi Roblox mati";
    public string UptimeText { get => _uptime; set => Set(ref _uptime, value); }
    public string UptimeSub { get => _uptimeSub; set => Set(ref _uptimeSub, value); }

    private void UpdateUptime()
    {
        if (_multiSince is not { } since) return;
        var d = DateTime.Now - since;
        UptimeText = $"{(int)d.TotalHours:00}:{d.Minutes:00}:{d.Seconds:00}";
        UptimeSub = $"sejak {since:HH:mm} WIB";
    }

    // ---------- Instance ----------

    public ObservableCollection<InstanceItem> Instances { get; } = new();
    private readonly Dictionary<int, string> _pidToAccount = new();
    public int InstanceCount => Instances.Count;
    public bool NoInstances => Instances.Count == 0;
    public RelayCommand RefreshInstancesCommand => _refreshInstancesCommand ??= new RelayCommand(RefreshInstances);
    private RelayCommand? _refreshInstancesCommand;
    public RelayCommand OpenRobloxNewCommand => _openRobloxNew ??= new RelayCommand(OpenRobloxNew);
    private RelayCommand? _openRobloxNew;

    private void OpenRobloxNew()
    {
        // Buka Roblox tanpa akun SupaBlox (sama seperti klik ikon Roblox Player).
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var versions = Path.Combine(local, "Roblox", "Versions");
        string? exe = Directory.Exists(versions)
            ? Directory.GetDirectories(versions).Select(d => Path.Combine(d, "RobloxPlayerBeta.exe")).Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (exe == null)
        {
            Launcher.Start("roblox-player:");
        }
        else Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        ShowMessage("Roblox dibuka (tanpa akun SupaBlox).");
    }

    private void RefreshInstances()
    {
        if (!OperatingSystem.IsWindows()) return;
        Process[] procs;
        try { procs = Process.GetProcessesByName(Launcher.ProcessName); }
        catch { return; }

        var alive = new HashSet<int>();
        foreach (var p in procs)
        {
            using (p)
            {
                alive.Add(p.Id);
                var item = Instances.FirstOrDefault(i => i.Pid == p.Id);
                if (item == null)
                {
                    item = new InstanceItem(p.Id, this);
                    try { item.StartTime = p.StartTime; item.StartText = p.StartTime.ToString("HH:mm:ss"); } catch { }
                    Instances.Add(item);
                }
                try { p.Refresh(); item.RamText = $"{p.WorkingSet64 / (1024 * 1024):N0} MB"; } catch { }
                try { var t = p.MainWindowTitle; item.Title = string.IsNullOrWhiteSpace(t) ? "Roblox (memuat…)" : t; } catch { }

                if (_pidToAccount.TryGetValue(p.Id, out var accId) && AllAccounts.FirstOrDefault(a => a.Model.Id == accId) is { } acc)
                {
                    item.AccountName = acc.Name;
                    item.AvatarBrush = acc.AvatarBrush;
                    acc.SetPid(p.Id);
                }
            }
        }

        foreach (var dead in Instances.Where(i => !alive.Contains(i.Pid)).ToList())
        {
            Instances.Remove(dead);
            if (_pidToAccount.Remove(dead.Pid, out var accId) && AllAccounts.FirstOrDefault(a => a.Model.Id == accId) is { } acc)
            {
                acc.SetPid(null);
                acc.SetLaunching(false);
            }
        }
        // Akun yang PID-nya hilang.
        foreach (var a in AllAccounts)
            if (a.RunningPid is int pid && !alive.Contains(pid)) a.SetPid(null);

        Notify(nameof(InstanceCount), nameof(NoInstances), nameof(MultiShort), nameof(InstanceCountText));
    }

    public string InstanceCountText => InstanceCount.ToString();

    public void FocusInstance(InstanceItem i)
    {
        try
        {
            using var p = Process.GetProcessById(i.Pid);
            var h = p.MainWindowHandle;
            if (h == IntPtr.Zero) { ShowMessage("Jendela Roblox belum siap.", true); return; }
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.SetForegroundWindow(h);
        }
        catch (Exception ex) { ShowMessage("Gagal fokus: " + ex.Message, true); }
    }

    /// <summary>WM_CLOSE ke jendela utama (sama seperti klik X). Tidak membunuh proses.</summary>
    public void CloseInstance(InstanceItem i)
    {
        try
        {
            using var p = Process.GetProcessById(i.Pid);
            if (!p.CloseMainWindow()) ShowMessage("Jendela Roblox tidak merespons. Tutup manual dari taskbar.", true);
            else ShowMessage($"Permintaan tutup dikirim ke PID {i.Pid}.");
        }
        catch (Exception ex) { ShowMessage("Gagal menutup: " + ex.Message, true); }
    }

    // ---------- Opsi aplikasi ----------

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set { _settings.MinimizeToTray = value; OnPropertyChanged(); QueueSave(); }
    }

    public bool MultiOnStart
    {
        get => _settings.MultiOnStart;
        set { _settings.MultiOnStart = value; OnPropertyChanged(); QueueSave(); }
    }

    private bool _startWithWindows;
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            try { StartupRegistry.Set(value); _startWithWindows = value; }
            catch (Exception ex) { ShowMessage("Gagal mengubah startup: " + ex.Message, true); }
            OnPropertyChanged();
        }
    }
}
