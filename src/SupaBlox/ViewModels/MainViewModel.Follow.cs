using System.Diagnostics;
using Avalonia.Threading;
using SupaBlox.Core;

namespace SupaBlox.ViewModels;

public sealed partial class MainViewModel
{
    // ================= Follow user =================

    public string FollowUsername
    {
        get => _settings.FollowUsername;
        set { if (_settings.FollowUsername == value) return; _settings.FollowUsername = value; OnPropertyChanged(); QueueSave(); }
    }

    private RelayCommand? _followCommand;
    public RelayCommand FollowCommand => _followCommand ??= new RelayCommand(() => FollowAsync(_settings.FollowUsername));

    /// <summary>
    /// Akun yang dicentang join ke server tempat user ini bermain.
    /// Presence dibaca dari sudut pandang akun SupaBlox (target harus terlihat: berteman / join terbuka).
    /// </summary>
    public async Task FollowAsync(string username)
    {
        username = username.Trim().TrimStart('@');
        if (username.Length == 0) { ShowMessage("Isi username yang mau di-follow.", true); return; }
        var accounts = SelectedAccounts;
        if (accounts.Count == 0) { ShowMessage("Centang akun yang mau ikut join dulu.", true); return; }

        long targetId;
        string targetName;
        var own = AllAccounts.FirstOrDefault(a => a.Model.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (own != null) { targetId = own.Model.UserId; targetName = own.Model.Username; }
        else
        {
            ShowMessage($"Mencari @{username}…");
            var u = await _api.GetUserByUsernameAsync(username);
            if (u == null) { ShowMessage($"Username @{username} tidak ditemukan.", true); return; }
            targetId = u.Id; targetName = u.Name;
        }

        // Coba presence dari akun yang dicentang dulu, lalu akun lain (mis. akun utama yang berteman dengan target).
        var viewers = accounts.Concat(AllAccounts).Where(a => !a.IsExpired && a.Model.UserId != targetId).Distinct().Take(4).ToList();
        if (own != null) viewers.Insert(0, own); // akun sendiri selalu bisa melihat presence-nya
        PresenceInfo? best = null;
        foreach (var v in viewers)
        {
            try
            {
                var cookie = Dpapi.Unprotect(v.Model.CookieProtected);
                var p = (await _api.GetPresenceAsync(cookie, new[] { targetId })).FirstOrDefault();
                if (p == null) continue;
                best ??= p;
                if (p.Type == 2 && p.PlaceId != null && !string.IsNullOrEmpty(p.GameId)) { best = p; break; }
            }
            catch (CookieExpiredException) { v.SetPresence(AccountState.Expired, "Cookie kedaluwarsa"); }
            catch (Exception ex) { Storage.Log("follow presence", ex); }
        }

        if (best == null) { ShowMessage($"Gagal membaca status @{targetName}.", true); return; }
        if (best.Type != 2) { ShowMessage($"@{targetName} sedang tidak di game ({(best.Type == 1 ? "online di website" : best.Type == 3 ? "di Studio" : "offline")}).", true); return; }
        if (best.PlaceId == null || string.IsNullOrEmpty(best.GameId))
        {
            ShowMessage($"@{targetName} di game, tapi servernya tersembunyi. Berteman dulu atau buka privasi join-nya.", true);
            return;
        }

        var target = new LaunchTarget(best.PlaceId.Value, JobId: best.GameId);
        var joiners = accounts.Where(a => a.Model.UserId != targetId).ToList();
        if (joiners.Count == 0) { ShowMessage("Akun yang dicentang hanya akun target itu sendiri.", true); return; }
        Enqueue(joiners.Select(a => (a, target)));
        ShowMessage($"Follow @{targetName}: {joiners.Count} akun join ke {best.Location} ({target.Describe()}).");
    }

    // ================= Watch + auto-relaunch =================
    // Hanya memantau proses (handle query/synchronize terbatas, seperti Task Manager). Tidak membaca/menulis memori Roblox.

    private readonly Dictionary<int, Process> _watchProcs = new();
    private readonly HashSet<int> _intentionalClose = new();
    private readonly Dictionary<string, LaunchTarget> _lastTarget = new();
    private readonly Dictionary<string, LaunchTarget> _lastServer = new();
    private readonly Dictionary<string, int> _relaunchCount = new();

    /// <summary>Dipanggil saat launch: simpan target agar relaunch bisa ke game/server yang sama.</summary>
    private void RememberTarget(AccountItem a, LaunchTarget t)
    {
        _lastTarget[a.Model.Id] = t;
        if (t.JobId != null || t.PrivateLinkCode != null) _lastServer[a.Model.Id] = t;
    }

    /// <summary>Presence akun sendiri memberi JobId server sekarang; dipakai untuk rejoin.</summary>
    private void RememberServer(AccountItem a, PresenceInfo p)
    {
        if (p.Type == 2 && p.PlaceId is long pl && !string.IsNullOrEmpty(p.GameId))
            _lastServer[a.Model.Id] = new LaunchTarget(pl, JobId: p.GameId);
    }

    private void WatchPid(int pid, AccountItem acc)
    {
        if (!OperatingSystem.IsWindows() || _watchProcs.ContainsKey(pid)) return;
        try
        {
            var p = Process.GetProcessById(pid);
            DateTime started = DateTime.Now;
            try { started = p.StartTime; } catch { }
            string accId = acc.Model.Id;
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) =>
            {
                int? code = null;
                try { code = p.ExitCode; } catch { }
                Dispatcher.UIThread.Post(() => OnWatchedExit(pid, accId, code, started));
            };
            _watchProcs[pid] = p;
        }
        catch (Exception ex) { Storage.Log("watch " + pid, ex); }
    }

    private void OnWatchedExit(int pid, string accId, int? code, DateTime started)
    {
        if (_watchProcs.Remove(pid, out var proc)) proc.Dispose();
        bool intentional = _intentionalClose.Remove(pid);
        var acc = AllAccounts.FirstOrDefault(a => a.Model.Id == accId);
        if (acc == null) return;
        var m = acc.Model;
        RefreshInstances();

        if (intentional || !m.Watch) return;
        bool normal = code == 0;
        string how = normal ? "ditutup" : code is int c ? $"crash (kode {c})" : "tertutup";

        // Jalan lama (>3 menit) = dianggap stabil, hitungan percobaan direset.
        if (DateTime.Now - started > TimeSpan.FromMinutes(3)) _relaunchCount[accId] = 0;

        if (!m.AutoRelaunch || (normal && !m.RelaunchOnNormalClose))
        {
            ShowMessage($"{acc.Name}: Roblox {how}.", !normal);
            return;
        }
        int n = _relaunchCount.GetValueOrDefault(accId) + 1;
        if (n > Math.Max(1, m.MaxRelaunch))
        {
            ShowMessage($"{acc.Name}: Roblox {how}. Auto-relaunch berhenti setelah {m.MaxRelaunch} percobaan.", true);
            return;
        }
        _relaunchCount[accId] = n;

        LaunchTarget? target = null;
        if (m.RejoinSameServer && _lastServer.TryGetValue(accId, out var srv)) target = srv;
        target ??= _lastTarget.TryGetValue(accId, out var t) ? new LaunchTarget(t.PlaceId) : null;
        if (target == null && m.DefaultPlaceId is long dp) target = new LaunchTarget(dp);
        if (target == null) { ShowMessage($"{acc.Name}: Roblox {how}, tapi tidak tahu game tujuan untuk relaunch.", true); return; }

        int delay = Math.Clamp(m.RelaunchDelaySec, 0, 600);
        ShowMessage($"{acc.Name}: Roblox {how}. Relaunch dalam {delay} dtk (percobaan {n}/{m.MaxRelaunch}).", !normal);
        var finalTarget = target;
        DispatcherTimer.RunOnce(() =>
        {
            if (acc.IsRunning || !AllAccounts.Contains(acc) || !acc.Model.AutoRelaunch) return;
            Enqueue(new[] { (acc, finalTarget) });
        }, TimeSpan.FromSeconds(Math.Max(delay, 1)));
    }

    /// <summary>Tutup yang disengaja (tombol Tutup / Multi OFF) tidak memicu relaunch.</summary>
    private void MarkIntentionalClose(int pid) => _intentionalClose.Add(pid);
}
