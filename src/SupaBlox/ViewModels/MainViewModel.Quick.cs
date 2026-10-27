using System.Collections.ObjectModel;
using SupaBlox.Core;

namespace SupaBlox.ViewModels;

public sealed partial class MainViewModel
{
    // ---------- Picker game (Kontrol Cepat) ----------

    public ObservableCollection<GameItem> PickerResults { get; } = new();
    public ObservableCollection<GameItem> RecentGames { get; } = new();

    public RelayCommand ClearQuickGameCommand { get; private set; } = null!;
    public RelayCommand LaunchSelectedCommand { get; private set; } = null!;
    public RelayCommand ClearQueueCommand { get; private set; } = null!;

    private void InitQuick()
    {
        ClearQuickGameCommand = new RelayCommand(() => SetQuickGame(null));
        LaunchSelectedCommand = new RelayCommand(_ => { EnqueueQuick(SelectedAccounts); return Task.CompletedTask; }, _ => SelectedCount > 0);
        ClearQueueCommand = new RelayCommand(() => { foreach (var q in Queue.ToList()) CancelQueueItem(q); });

        if (_settings.QuickGame is { } g) _quickGame = new GameItem(g, _ => { });
        _quickJob = _settings.QuickJobId;
        RebuildRecent();
        if (_quickGame != null) _ = LoadGameIconsAsync(new[] { _quickGame });
    }

    private string _pickerQuery = "";
    private CancellationTokenSource? _pickerCts;
    public string PickerQuery
    {
        get => _pickerQuery;
        set { if (Set(ref _pickerQuery, value)) _ = RunPickerSearchAsync(value); }
    }

    private bool _isPickerOpen;
    public bool IsPickerOpen { get => _isPickerOpen; set => Set(ref _isPickerOpen, value); }

    private string _pickerInfo = "";
    public string PickerInfo { get => _pickerInfo; set { if (Set(ref _pickerInfo, value)) Notify(nameof(HasPickerInfo)); } }
    public bool HasPickerInfo => _pickerInfo.Length > 0;

    private async Task RunPickerSearchAsync(string text)
    {
        _pickerCts?.Cancel();
        var cts = _pickerCts = new CancellationTokenSource();
        text = text.Trim();
        if (text.Length < 2) { PickerResults.Clear(); IsPickerOpen = false; return; }
        try
        {
            await Task.Delay(350, cts.Token);
            PickerInfo = "Mencari…";
            IsPickerOpen = true;
            List<GameRef> found;
            if (Launcher.ParsePlaceId(text) is long placeId)
            {
                var g = await _api.ResolvePlaceAsync(placeId, cts.Token);
                found = g != null ? new List<GameRef> { g } : new List<GameRef>();
            }
            else found = await _api.SearchGamesAsync(text, 8, cts.Token);
            if (cts.IsCancellationRequested) return;

            PickerResults.Clear();
            foreach (var g in found) PickerResults.Add(new GameItem(g, PickGame, IsFavoriteGame(g.PlaceId)));
            if (PickerResults.Count > 0) PickerResults[0].IsCurrent = true;
            PickerInfo = found.Count == 0 ? "Game tidak ditemukan." : "";
            await LoadGameIconsAsync(PickerResults.ToList());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Storage.Log("picker", ex); PickerInfo = "Gagal mencari: " + FriendlyError(ex); }
    }

    /// <summary>Enter di kotak pencarian = pakai hasil teratas.</summary>
    public void PickFirst()
    {
        if (PickerResults.FirstOrDefault() is { } first) PickGame(first);
    }

    public void ClosePicker() => IsPickerOpen = false;

    private void PickGame(GameItem item)
    {
        SetQuickGame(item.Model);
        _pickerCts?.Cancel();
        _pickerQuery = "";
        OnPropertyChanged(nameof(PickerQuery));
        PickerResults.Clear();
        IsPickerOpen = false;
    }

    private GameItem? _quickGame;
    public GameItem? QuickGame { get => _quickGame; private set { if (Set(ref _quickGame, value)) Notify(nameof(HasQuickGame), nameof(NoQuickGame)); } }
    public bool HasQuickGame => _quickGame != null;
    public bool NoQuickGame => _quickGame == null;

    public void SetQuickGame(GameRef? g)
    {
        _settings.QuickGame = g;
        if (g != null) AddRecent(g);
        QuickGame = g == null ? null : new GameItem(g, _ => { });
        if (QuickGame != null) _ = LoadGameIconsAsync(new[] { QuickGame });
        QueueSave();
    }

    private void AddRecent(GameRef g)
    {
        _settings.RecentGames.RemoveAll(r => r.PlaceId == g.PlaceId);
        _settings.RecentGames.Insert(0, g);
        if (_settings.RecentGames.Count > 8) _settings.RecentGames.RemoveRange(8, _settings.RecentGames.Count - 8);
        RebuildRecent();
    }

    private void RebuildRecent()
    {
        RecentGames.Clear();
        foreach (var r in _settings.RecentGames.Take(4)) RecentGames.Add(new GameItem(r, i => SetQuickGame(i.Model)));
        Notify(nameof(HasRecent));
    }

    public bool HasRecent => RecentGames.Count > 0;

    private string _quickJob = "";
    public string QuickJobId
    {
        get => _quickJob;
        set { if (Set(ref _quickJob, value)) { _settings.QuickJobId = value; QueueSave(); } }
    }

    public decimal? LaunchDelay
    {
        get => _settings.LaunchDelaySec;
        set { _settings.LaunchDelaySec = (int)Math.Clamp(value ?? 8, 0, 120); OnPropertyChanged(); QueueSave(); }
    }

    public decimal? MaxInstances
    {
        get => _settings.MaxInstances;
        set { _settings.MaxInstances = (int)Math.Clamp(value ?? 6, 1, 50); OnPropertyChanged(); QueueSave(); }
    }

    public bool UseDefaultGame
    {
        get => _settings.UseDefaultGame;
        set { _settings.UseDefaultGame = value; OnPropertyChanged(); QueueSave(); }
    }

    public bool HideUsernames
    {
        get => _settings.HideUsernames;
        set
        {
            _settings.HideUsernames = value;
            OnPropertyChanged();
            QueueSave();
            foreach (var a in AllAccounts) a.RefreshTexts();
        }
    }

    // ---------- Antrean launch ----------

    public ObservableCollection<QueueItem> Queue { get; } = new();
    public bool QueueEmpty => Queue.Count == 0;
    private bool _queueRunning;

    /// <summary>Launch dari Kontrol Cepat / tombol Launch kartu: pakai game terpilih + Job ID/link.</summary>
    public async void EnqueueQuick(IEnumerable<AccountItem> accounts)
    {
        var list = accounts.ToList();
        if (list.Count == 0) { ShowMessage("Centang akun dulu.", true); return; }
        try
        {
            var extra = _quickJob.Trim();
            LaunchTarget? shared = null;

            if (extra.Length > 0)
            {
                if (Launcher.ParseShareCode(extra) is { } share)
                {
                    var cookie = Dpapi.Unprotect(list[0].Model.CookieProtected);
                    var (pid, code) = await _api.ResolveShareLinkAsync(cookie, share);
                    shared = new LaunchTarget(pid, PrivateLinkCode: code);
                }
                else if (Launcher.ParsePrivateLink(extra) is { Code: { } code } priv)
                {
                    var pid = priv.PlaceId ?? _quickGame?.Model.PlaceId;
                    if (pid == null) { ShowMessage("Link server privat butuh Place ID. Pilih game atau pakai link lengkap.", true); return; }
                    shared = new LaunchTarget(pid.Value, PrivateLinkCode: code);
                }
                else if (Launcher.IsJobId(extra))
                {
                    if (_quickGame == null) { ShowMessage("Job ID butuh game. Pilih game dulu di kolom Game.", true); return; }
                    shared = new LaunchTarget(_quickGame.Model.PlaceId, JobId: extra);
                }
                else { ShowMessage("Isi kolom kedua tidak dikenali. Masukkan Job ID atau link server privat, atau kosongkan.", true); return; }
            }

            int skipped = 0;
            var targets = new List<(AccountItem, LaunchTarget)>();
            foreach (var a in list)
            {
                LaunchTarget? t = shared;
                if (t == null)
                {
                    long? pid = _quickGame?.Model.PlaceId ?? (UseDefaultGame ? a.Model.DefaultPlaceId : null);
                    if (pid == null) { skipped++; continue; }
                    t = new LaunchTarget(pid.Value);
                }
                targets.Add((a, t));
            }
            if (targets.Count == 0) { ShowMessage("Pilih game dulu (kolom Game di Kontrol Cepat).", true); return; }
            Enqueue(targets);
            if (skipped > 0) ShowMessage($"{skipped} akun dilewati karena tidak punya game default.", true);
        }
        catch (Exception ex) { Storage.Log("enqueue", ex); ShowMessage(FriendlyError(ex), true); }
    }

    public void Enqueue(IEnumerable<(AccountItem Account, LaunchTarget Target)> items)
    {
        int added = 0;
        foreach (var (acc, target) in items)
        {
            if (acc.IsRunning || Queue.Any(q => q.Account == acc)) continue;
            var qi = new QueueItem(acc, target, CancelQueueItem);
            Queue.Add(qi);
            acc.SetLaunching(true);
            added++;
        }
        Notify(nameof(QueueEmpty));
        if (added > 0 && !_multiEnabled && Queue.Count > 1)
            ShowMessage("Multi Roblox OFF: instance kedua akan menutup yang pertama. Nyalakan Multi Roblox.", true);
        if (!_queueRunning) _ = RunQueueAsync();
    }

    private CancellationTokenSource _queueCts = new();

    private void CancelQueueItem(QueueItem q)
    {
        if (Queue.Remove(q)) q.Account.SetLaunching(false);
        if (q.Status.StartsWith("minta") || q.Status.StartsWith("membuka")) { _queueCts.Cancel(); }
        Notify(nameof(QueueEmpty));
    }

    private async Task RunQueueAsync()
    {
        _queueRunning = true;
        try
        {
            bool first = true;
            while (Queue.Count > 0)
            {
                var item = Queue[0];
                if (!first)
                {
                    int delay = _settings.LaunchDelaySec;
                    for (int s = delay; s > 0 && Queue.Contains(item); s--)
                    {
                        item.Status = $"antre ({s} dtk)";
                        await Task.Delay(1000);
                    }
                    if (!Queue.Contains(item)) continue;
                }

                while (Launcher.RunningPids().Count >= _settings.MaxInstances && Queue.Contains(item))
                {
                    item.Status = $"menunggu slot (maks {_settings.MaxInstances})";
                    item.StatusBrush = Palette.Yellow;
                    await Task.Delay(2000);
                }
                if (!Queue.Contains(item)) continue;

                _queueCts = new CancellationTokenSource();
                item.Status = "minta ticket…";
                item.StatusBrush = Palette.Yellow;
                try
                {
                    var cookie = Dpapi.Unprotect(item.Account.Model.CookieProtected);
                    var ticket = await _api.GetAuthTicketAsync(cookie, _queueCts.Token);
                    if (!Queue.Contains(item)) continue;
                    var before = Launcher.RunningPids();
                    item.Status = "membuka Roblox…";
                    Launcher.Start(Launcher.BuildUri(ticket, item.Target));
                    RememberTarget(item.Account, item.Target);
                    _ = TrackNewPidAsync(item.Account, before);
                    ShowMessage($"{item.Account.Name} diluncurkan ke {item.Target.Describe()}.");
                }
                catch (OperationCanceledException) { item.Account.SetLaunching(false); }
                catch (CookieExpiredException)
                {
                    item.Account.SetPresence(AccountState.Expired, "Cookie kedaluwarsa");
                    item.Account.SetLaunching(false);
                    ShowMessage($"{item.Account.Name}: cookie kedaluwarsa, perbarui lewat ⋯.", true);
                }
                catch (Exception ex)
                {
                    Storage.Log("launch " + item.Account.Model.Username, ex);
                    item.Account.SetLaunching(false);
                    ShowMessage($"{item.Account.Name}: {FriendlyError(ex)}", true);
                }
                Queue.Remove(item);
                Notify(nameof(QueueEmpty));
                first = false;
            }
        }
        finally { _queueRunning = false; Notify(nameof(QueueEmpty)); }
    }

    /// <summary>Petakan PID RobloxPlayerBeta baru ke akun (hanya membaca daftar proses).</summary>
    private async Task TrackNewPidAsync(AccountItem account, HashSet<int> before)
    {
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(750);
            var now = Launcher.RunningPids();
            var fresh = now.Where(p => !before.Contains(p) && !_pidToAccount.ContainsKey(p)).ToList();
            if (fresh.Count > 0)
            {
                _pidToAccount[fresh[0]] = account.Model.Id;
                account.SetPid(fresh[0]);
                if (account.Model.Watch) WatchPid(fresh[0], account);
                RefreshInstances();
                return;
            }
        }
        account.SetLaunching(false);
        ShowMessage($"{account.Name}: Roblox tidak terdeteksi terbuka dalam 30 dtk.", true);
    }
}
