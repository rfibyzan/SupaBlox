using System.Collections.ObjectModel;
using SupaBlox.Core;

namespace SupaBlox.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<AccountItem> AllAccounts { get; } = new();
    public ObservableCollection<AccountItem> VisibleAccounts { get; } = new();
    public string[] StatusOptions { get; } = { "Semua status", "Berjalan", "Online / di game", "Offline", "Bermasalah" };

    public RelayCommand OpenAddCommand { get; private set; } = null!;
    public RelayCommand OpenBulkCommand { get; private set; } = null!;
    public RelayCommand RefreshStatusCommand { get; private set; } = null!;
    public RelayCommand SelectAllCommand { get; private set; } = null!;
    public RelayCommand ClearSelectionCommand { get; private set; } = null!;

    // Dialog tambah
    public RelayCommand CloseAddCommand { get; private set; } = null!;
    public RelayCommand AddTabCommand { get; private set; } = null!;
    public RelayCommand BrowserLoginCommand { get; private set; } = null!;
    public RelayCommand CancelBrowserLoginCommand { get; private set; } = null!;
    public RelayCommand AddSaveCommand { get; private set; } = null!;

    // Dialog edit
    public RelayCommand CloseEditCommand { get; private set; } = null!;
    public RelayCommand EditSaveCommand { get; private set; } = null!;
    public RelayCommand EditDeleteCommand { get; private set; } = null!;

    private void InitCommands()
    {
        DismissMessageCommand = new RelayCommand(() => Message = "");
    }

    private void InitAccounts()
    {
        OpenAddCommand = new RelayCommand(() => OpenAdd(0));
        OpenBulkCommand = new RelayCommand(() => OpenAdd(2));
        RefreshStatusCommand = new RelayCommand(() => RefreshAllStatusAsync(silent: false));
        SelectAllCommand = new RelayCommand(() => { foreach (var a in VisibleAccounts) a.SetSelectedSilently(true); OnSelectionChanged(); });
        ClearSelectionCommand = new RelayCommand(() => { foreach (var a in AllAccounts) a.SetSelectedSilently(false); OnSelectionChanged(); });

        CloseAddCommand = new RelayCommand(() => { _loginCts?.Cancel(); IsAddOpen = false; });
        AddTabCommand = new RelayCommand(p => AddTab = int.TryParse(p?.ToString(), out var t) ? t : 0);
        BrowserLoginCommand = new RelayCommand(BrowserLoginAsync);
        CancelBrowserLoginCommand = new RelayCommand(() => _loginCts?.Cancel());
        AddSaveCommand = new RelayCommand(AddSaveAsync);

        CloseEditCommand = new RelayCommand(() => IsEditOpen = false);
        EditSaveCommand = new RelayCommand(EditSaveAsync);
        EditDeleteCommand = new RelayCommand(EditDelete);

        var selected = new HashSet<string>(_settings.SelectedAccountIds);
        foreach (var acc in _accounts)
        {
            var item = new AccountItem(this, acc);
            item.SetSelectedSilently(selected.Contains(acc.Id));
            AllAccounts.Add(item);
        }
        RebuildGroups();
        RebuildVisibleAccounts();
        _ = LoadAvatarsAsync(AllAccounts.ToList());
    }

    public int AccountCount => AllAccounts.Count;
    public bool NoAccounts => AllAccounts.Count == 0;
    public string AccountsSubtitle => $"{AllAccounts.Count} akun tersimpan • cookie terenkripsi DPAPI lokal";

    // ---------- Filter ----------

    private string _accountFilter = "";
    public string AccountFilter { get => _accountFilter; set { if (Set(ref _accountFilter, value)) RebuildVisibleAccounts(); } }

    private int _statusFilter;
    public int StatusFilter { get => _statusFilter; set { if (Set(ref _statusFilter, value)) RebuildVisibleAccounts(); } }

    private string? _groupFilter = "Semua grup";
    public string? GroupFilter
    {
        get => _groupFilter;
        set
        {
            if (!Set(ref _groupFilter, value)) return;
            foreach (var c in GroupChips) c.IsActive = c.Name == (value ?? AllGroupsName);
            RebuildVisibleAccounts();
        }
    }

    public void RebuildVisibleAccounts()
    {
        var q = _accountFilter.Trim();
        IEnumerable<AccountItem> list = AllAccounts;
        if (q.Length > 0)
            list = list.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                   a.Model.Username.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                   a.Model.Note.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (_groupFilter == NoGroupName)
            list = list.Where(a => string.IsNullOrWhiteSpace(a.Model.Group));
        else if (_groupFilter is { } g && g != AllGroupsName)
            list = list.Where(a => a.Model.Group == g);
        list = _statusFilter switch
        {
            1 => list.Where(a => a.IsRunning),
            2 => list.Where(a => a.State is AccountState.Online or AccountState.InGame or AccountState.Studio),
            3 => list.Where(a => a.State is AccountState.Offline or AccountState.Unknown && !a.IsRunning),
            4 => list.Where(a => a.IsExpired),
            _ => list
        };
        var result = list.OrderByDescending(a => a.Model.Favorite).ThenBy(a => a.Model.Group).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

        VisibleAccounts.Clear();
        foreach (var a in result) VisibleAccounts.Add(a);
        Notify(nameof(AccountCount), nameof(NoAccounts), nameof(AccountsSubtitle), nameof(VisibleHeader));
    }

    public string VisibleHeader => VisibleAccounts.Count == AllAccounts.Count ? "Semua akun" : $"{VisibleAccounts.Count} dari {AllAccounts.Count} akun";

    private void RebuildGroups()
    {
        var names = AllGroupNames();
        if (_groupFilter is { } cur && cur != AllGroupsName && cur != NoGroupName && !names.Contains(cur))
        {
            _groupFilter = AllGroupsName;
            OnPropertyChanged(nameof(GroupFilter));
        }
        RebuildGroupChips();
    }

    // ---------- Seleksi ----------

    public List<AccountItem> SelectedAccounts => AllAccounts.Where(a => a.IsSelected).ToList();
    public int SelectedCount => AllAccounts.Count(a => a.IsSelected);
    public string SelectedBadge => SelectedCount == 0 ? "0 akun" : $"{SelectedCount} akun";
    public string SelectedNames
    {
        get
        {
            var s = SelectedAccounts;
            if (s.Count == 0) return "Belum ada akun dipilih (centang di tab Akun)";
            var names = string.Join(", ", s.Take(3).Select(a => a.Name));
            return s.Count > 3 ? $"{names} +{s.Count - 3}" : names;
        }
    }

    public void OnSelectionChanged()
    {
        _settings.SelectedAccountIds = AllAccounts.Where(a => a.IsSelected).Select(a => a.Model.Id).ToList();
        QueueSave();
        Notify(nameof(SelectedCount), nameof(SelectedBadge), nameof(SelectedNames), nameof(SelectedAccounts));
        LaunchSelectedCommand?.Refresh();
    }

    // ---------- Avatar & status ----------

    private async Task LoadAvatarsAsync(List<AccountItem> items)
    {
        try
        {
            var urls = await _api.GetAvatarHeadshotsAsync(items.Select(i => i.Model.UserId));
            foreach (var i in items)
                if (urls.TryGetValue(i.Model.UserId, out var u)) i.Avatar = await LoadImageAsync(u);
        }
        catch (Exception ex) { Storage.Log("avatars", ex); }
    }

    private bool _refreshingStatus;
    public bool RefreshingStatus { get => _refreshingStatus; set => Set(ref _refreshingStatus, value); }

    public async Task RefreshAllStatusAsync(bool silent)
    {
        if (_refreshingStatus || AllAccounts.Count == 0 || !OperatingSystem.IsWindows()) return;
        RefreshingStatus = true;
        int expired = 0;
        try
        {
            foreach (var a in AllAccounts.ToList())
            {
                try
                {
                    var cookie = Dpapi.Unprotect(a.Model.CookieProtected);
                    var p = (await _api.GetPresenceAsync(cookie, new[] { a.Model.UserId })).FirstOrDefault();
                    if (p == null) { a.SetPresence(AccountState.Unknown, "Status tidak diketahui"); continue; }
                    RememberServer(a, p);
                    switch (p.Type)
                    {
                        case 2: a.SetPresence(AccountState.InGame, "Di game" + (string.IsNullOrWhiteSpace(p.Location) ? "" : " • " + p.Location)); break;
                        case 1: a.SetPresence(AccountState.Online, "Online (website)"); break;
                        case 3: a.SetPresence(AccountState.Studio, "Roblox Studio"); break;
                        default: a.SetPresence(AccountState.Offline, "Offline"); break;
                    }
                }
                catch (CookieExpiredException) { a.SetPresence(AccountState.Expired, "Cookie kedaluwarsa"); expired++; }
                catch (Exception ex) { Storage.Log("presence " + a.Model.Username, ex); a.SetPresence(AccountState.Unknown, "Gagal cek status"); }
            }
            if (!silent) ShowMessage(expired > 0 ? $"Status diperbarui. {expired} akun cookie-nya kedaluwarsa." : "Status semua akun diperbarui.", expired > 0);
        }
        finally
        {
            RefreshingStatus = false;
            if (_statusFilter != 0) RebuildVisibleAccounts();
        }
    }

    // ---------- Dialog Tambah Akun ----------

    private bool _isAddOpen;
    public bool IsAddOpen { get => _isAddOpen; set => Set(ref _isAddOpen, value); }

    private int _addTab;
    public int AddTab
    {
        get => _addTab;
        set { if (Set(ref _addTab, value)) Notify(nameof(IsAddTab0), nameof(IsAddTab1), nameof(IsAddTab2), nameof(AddSaveText), nameof(ShowAddSave)); }
    }
    public bool IsAddTab0 => _addTab == 0;
    public bool IsAddTab1 => _addTab == 1;
    public bool IsAddTab2 => _addTab == 2;
    public bool ShowAddSave => _addTab != 0;
    public string AddSaveText => _addTab == 2 ? "Impor akun" : "Simpan akun";

    private string _addCookie = "";
    public string AddCookie { get => _addCookie; set => Set(ref _addCookie, value); }
    private string _addBulk = "";
    public string AddBulk { get => _addBulk; set => Set(ref _addBulk, value); }
    private string _addGroup = "";
    public string AddGroup { get => _addGroup; set => Set(ref _addGroup, value); }
    private string _addPlace = "";
    public string AddPlace { get => _addPlace; set => Set(ref _addPlace, value); }
    private string _addNote = "";
    public string AddNote { get => _addNote; set => Set(ref _addNote, value); }
    private string _addStatus = "";
    public string AddStatus { get => _addStatus; set { if (Set(ref _addStatus, value)) Notify(nameof(HasAddStatus)); } }
    public bool HasAddStatus => _addStatus.Length > 0;
    private bool _addBusy;
    public bool AddBusy { get => _addBusy; set { if (Set(ref _addBusy, value)) Notify(nameof(AddIdle)); } }
    public bool AddIdle => !_addBusy;

    private CancellationTokenSource? _loginCts;

    private void OpenAdd(int tab)
    {
        AddTab = tab;
        AddCookie = AddBulk = AddPlace = AddNote = AddStatus = "";
        AddGroup = _groupFilter is { } g && g != AllGroupsName && g != NoGroupName ? g : "";
        IsAddOpen = true;
    }

    private async Task BrowserLoginAsync()
    {
        _loginCts?.Cancel();
        _loginCts = new CancellationTokenSource();
        AddBusy = true;
        try
        {
            var cookie = await LoginBrowser.CaptureCookieAsync(s => AddStatus = s, _loginCts.Token);
            if (cookie == null) { AddStatus = "Jendela login ditutup sebelum login selesai."; return; }
            var item = await AddOrUpdateFromCookieAsync(cookie, AddGroup, AddPlace, AddNote);
            AddStatus = "";
            IsAddOpen = false;
            ShowMessage($"Akun {item.Name} ditambahkan.");
        }
        catch (OperationCanceledException) { AddStatus = "Login dibatalkan."; }
        catch (Exception ex) { Storage.Log("browser login", ex); AddStatus = FriendlyError(ex); }
        finally { AddBusy = false; }
    }

    private async Task AddSaveAsync()
    {
        AddBusy = true;
        try
        {
            if (_addTab == 1)
            {
                var cookie = ExtractCookie(AddCookie);
                if (cookie == null) { AddStatus = "Cookie tidak valid. Tempel nilai .ROBLOSECURITY lengkap (diawali _|WARNING:-DO-NOT-SHARE-THIS…)."; return; }
                AddStatus = "Memeriksa cookie…";
                var item = await AddOrUpdateFromCookieAsync(cookie, AddGroup, AddPlace, AddNote);
                IsAddOpen = false;
                ShowMessage($"Akun {item.Name} ditambahkan.");
            }
            else if (_addTab == 2)
            {
                var lines = AddBulk.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                int ok = 0, bad = 0, noCookie = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    AddStatus = $"Mengimpor {i + 1}/{lines.Length}…";
                    var cookie = ExtractCookie(lines[i]);
                    if (cookie == null) { noCookie++; continue; }
                    try { await AddOrUpdateFromCookieAsync(cookie, AddGroup, AddPlace, AddNote); ok++; }
                    catch (Exception ex) { Storage.Log("bulk import", ex); bad++; }
                }
                var msg = $"Impor selesai: {ok} berhasil";
                if (bad > 0) msg += $", {bad} cookie invalid";
                if (noCookie > 0) msg += $", {noCookie} baris tanpa cookie dilewati (user:pass saja tidak bisa, Roblox wajib captcha)";
                AddStatus = msg + ".";
                if (ok > 0 && bad == 0 && noCookie == 0) { IsAddOpen = false; ShowMessage(msg + "."); }
            }
        }
        catch (Exception ex) { Storage.Log("add", ex); AddStatus = FriendlyError(ex); }
        finally { AddBusy = false; }
    }

    /// <summary>Ambil nilai .ROBLOSECURITY dari baris apa pun (cookie saja, user:pass:cookie, atau ".ROBLOSECURITY=...").</summary>
    private static string? ExtractCookie(string text)
    {
        text = text.Trim().Trim('"');
        int idx = text.IndexOf("_|WARNING:", StringComparison.Ordinal);
        if (idx < 0) return null;
        var c = text[idx..];
        int end = c.IndexOfAny(new[] { ';', ' ', '\t', '\r', '\n', '"' });
        if (end > 0) c = c[..end];
        return c.Length > 100 ? c : null;
    }

    private async Task<AccountItem> AddOrUpdateFromCookieAsync(string cookie, string group, string placeText, string note)
    {
        var user = await _api.GetAuthenticatedUserAsync(cookie);
        long? placeId = string.IsNullOrWhiteSpace(placeText) ? null : Launcher.ParsePlaceId(placeText);
        string placeName = "";
        if (placeId is long pid)
        {
            try { placeName = (await _api.ResolvePlaceAsync(pid))?.Name ?? ""; } catch { }
        }

        var existing = AllAccounts.FirstOrDefault(a => a.Model.UserId == user.Id);
        AccountItem item;
        if (existing != null)
        {
            existing.Model.CookieProtected = Dpapi.Protect(cookie);
            existing.Model.Username = user.Name;
            existing.Model.DisplayName = user.DisplayName;
            if (!string.IsNullOrWhiteSpace(group)) existing.Model.Group = group.Trim();
            if (placeId != null) { existing.Model.DefaultPlaceId = placeId; existing.Model.DefaultPlaceName = placeName; }
            if (!string.IsNullOrWhiteSpace(note)) existing.Model.Note = note.Trim();
            item = existing;
            item.RefreshTexts();
        }
        else
        {
            var acc = new Account
            {
                UserId = user.Id,
                Username = user.Name,
                DisplayName = user.DisplayName,
                Group = group.Trim(),
                DefaultPlaceId = placeId,
                DefaultPlaceName = placeName,
                Note = note.Trim(),
                CookieProtected = Dpapi.Protect(cookie),
            };
            _accounts.Add(acc);
            item = new AccountItem(this, acc);
            AllAccounts.Add(item);
        }
        item.SetPresence(AccountState.Offline, "Siap");
        SaveAccounts();
        RebuildGroups();
        RebuildVisibleAccounts();
        _ = LoadAvatarsAsync(new List<AccountItem> { item });
        return item;
    }

    // ---------- Dialog Edit ----------

    private bool _isEditOpen;
    public bool IsEditOpen { get => _isEditOpen; set => Set(ref _isEditOpen, value); }
    private AccountItem? _editing;
    public AccountItem? Editing { get => _editing; private set => Set(ref _editing, value); }
    private string _editAlias = "", _editGroup = "", _editPlace = "", _editNote = "", _editCookie = "", _editStatus = "";
    public string EditAlias { get => _editAlias; set => Set(ref _editAlias, value); }
    public string EditGroup { get => _editGroup; set => Set(ref _editGroup, value); }
    public string EditPlace { get => _editPlace; set => Set(ref _editPlace, value); }
    public string EditNote { get => _editNote; set => Set(ref _editNote, value); }
    public string EditCookie { get => _editCookie; set => Set(ref _editCookie, value); }
    public string EditStatus { get => _editStatus; set { if (Set(ref _editStatus, value)) Notify(nameof(HasEditStatus)); } }
    public bool HasEditStatus => _editStatus.Length > 0;
    private bool _editFavorite;
    public bool EditFavorite { get => _editFavorite; set => Set(ref _editFavorite, value); }
    private bool _confirmDelete;
    public bool ConfirmDelete { get => _confirmDelete; set { if (Set(ref _confirmDelete, value)) Notify(nameof(DeleteText)); } }
    public string DeleteText => _confirmDelete ? "Yakin? Klik lagi untuk hapus" : "Hapus akun";

    public void OpenEdit(AccountItem item)
    {
        Editing = item;
        EditAlias = item.Model.Alias;
        EditGroup = item.Model.Group;
        EditPlace = item.Model.DefaultPlaceId?.ToString() ?? "";
        EditNote = item.Model.Note;
        EditFavorite = item.Model.Favorite;
        EditWatch = item.Model.Watch;
        EditAutoRelaunch = item.Model.AutoRelaunch;
        EditRelaunchDelay = item.Model.RelaunchDelaySec;
        EditMaxRelaunch = item.Model.MaxRelaunch;
        EditRelaunchNormal = item.Model.RelaunchOnNormalClose;
        EditRejoin = item.Model.RejoinSameServer;
        ProfileDescription = "";
        _profileLoaded = false;
        ProfileStatus = "";
        Notify(nameof(ProfileInfo));
        _ = LoadProfileAsync(item, silent: true);
        EditCookie = "";
        EditStatus = "";
        ConfirmDelete = false;
        IsEditOpen = true;
    }

    private async Task EditSaveAsync()
    {
        if (_editing is not { } item) return;
        var m = item.Model;

        if (!string.IsNullOrWhiteSpace(EditCookie))
        {
            var cookie = ExtractCookie(EditCookie);
            if (cookie == null) { EditStatus = "Cookie baru tidak valid."; return; }
            EditStatus = "Memeriksa cookie baru…";
            var user = await _api.GetAuthenticatedUserAsync(cookie);
            if (user.Id != m.UserId) { EditStatus = $"Cookie ini milik @{user.Name}, bukan @{m.Username}."; return; }
            m.CookieProtected = Dpapi.Protect(cookie);
            item.SetPresence(AccountState.Offline, "Cookie diperbarui");
        }

        long? placeId = string.IsNullOrWhiteSpace(EditPlace) ? null : Launcher.ParsePlaceId(EditPlace);
        if (!string.IsNullOrWhiteSpace(EditPlace) && placeId == null) { EditStatus = "Game default harus Place ID atau link roblox.com/games/…"; return; }
        if (placeId != m.DefaultPlaceId)
        {
            m.DefaultPlaceId = placeId;
            m.DefaultPlaceName = "";
            if (placeId is long pid)
            {
                try { m.DefaultPlaceName = (await _api.ResolvePlaceAsync(pid))?.Name ?? ""; } catch { }
            }
        }
        m.Alias = EditAlias.Trim();
        m.Group = EditGroup.Trim();
        m.Note = EditNote.Trim();
        m.Favorite = EditFavorite;
        m.Watch = EditWatch;
        m.AutoRelaunch = EditWatch && EditAutoRelaunch;
        m.RelaunchDelaySec = (int)Math.Clamp(EditRelaunchDelay ?? 10, 0, 600);
        m.MaxRelaunch = (int)Math.Clamp(EditMaxRelaunch ?? 3, 1, 50);
        m.RelaunchOnNormalClose = EditRelaunchNormal;
        m.RejoinSameServer = EditRejoin;
        if (m.Watch && item.RunningPid is int runPid) WatchPid(runPid, item);
        _relaunchCount.Remove(m.Id);
        if (!string.IsNullOrWhiteSpace(m.Group) && !_settings.Groups.Contains(m.Group)) { _settings.Groups.Add(m.Group); QueueSave(); }
        SaveAccounts();
        item.RefreshTexts();
        RebuildGroups();
        RebuildVisibleAccounts();
        IsEditOpen = false;
        ShowMessage($"Akun {item.Name} disimpan.");
    }

    // Watch + auto-relaunch (dialog edit)
    private bool _editWatch, _editAutoRelaunch, _editRelaunchNormal, _editRejoin = true;
    private decimal? _editRelaunchDelay = 10, _editMaxRelaunch = 3;
    public bool EditWatch { get => _editWatch; set => Set(ref _editWatch, value); }
    public bool EditAutoRelaunch { get => _editAutoRelaunch; set => Set(ref _editAutoRelaunch, value); }
    public bool EditRelaunchNormal { get => _editRelaunchNormal; set => Set(ref _editRelaunchNormal, value); }
    public bool EditRejoin { get => _editRejoin; set => Set(ref _editRejoin, value); }
    public decimal? EditRelaunchDelay { get => _editRelaunchDelay; set => Set(ref _editRelaunchDelay, value); }
    public decimal? EditMaxRelaunch { get => _editMaxRelaunch; set => Set(ref _editMaxRelaunch, value); }

    // Profil Roblox (dialog edit)
    private string _profileDescription = "", _profileStatus = "";
    private bool _profileLoaded, _profileBusy;
    public string ProfileDescription { get => _profileDescription; set { if (Set(ref _profileDescription, value)) Notify(nameof(ProfileCounter)); } }
    public string ProfileCounter => $"{_profileDescription.Length}/1000";
    public string ProfileStatus { get => _profileStatus; set { if (Set(ref _profileStatus, value)) Notify(nameof(HasProfileStatus)); } }
    public bool HasProfileStatus => _profileStatus.Length > 0;
    public bool ProfileIdle => !_profileBusy;
    public string ProfileInfo => _editing is not { } e ? "" :
        $"ID {e.Model.UserId}" + (e.Model.CreatedUtc is DateTime c ? $"  •  dibuat {c.ToLocalTime():d MMM yyyy}" : "");

    private RelayCommand? _profileRefresh, _profileSave;
    public RelayCommand ProfileRefreshCommand => _profileRefresh ??= new RelayCommand(() => _editing is { } e ? LoadProfileAsync(e, silent: false) : Task.CompletedTask);
    public RelayCommand ProfileSaveCommand => _profileSave ??= new RelayCommand(SaveDescriptionAsync);

    private void SetProfileBusy(bool v) { _profileBusy = v; Notify(nameof(ProfileIdle)); }

    /// <summary>Refresh profil publik (nama, display name, tanggal dibuat) + deskripsi akun.</summary>
    private async Task LoadProfileAsync(AccountItem item, bool silent)
    {
        if (!OperatingSystem.IsWindows() && silent) return;
        SetProfileBusy(true);
        if (!silent) ProfileStatus = "Mengambil profil…";
        try
        {
            var (user, created) = await _api.GetUserInfoAsync(item.Model.UserId);
            bool changed = user.Name.Length > 0 && (user.Name != item.Model.Username || user.DisplayName != item.Model.DisplayName);
            if (user.Name.Length > 0) item.Model.Username = user.Name;
            if (user.DisplayName.Length > 0) item.Model.DisplayName = user.DisplayName;
            item.Model.CreatedUtc = created;
            var cookie = Dpapi.Unprotect(item.Model.CookieProtected);
            var desc = await _api.GetDescriptionAsync(cookie);
            if (_editing == item) { ProfileDescription = desc; _profileLoaded = true; }
            SaveAccounts();
            item.RefreshTexts();
            if (changed) RebuildVisibleAccounts();
            Notify(nameof(ProfileInfo));
            if (_editing == item) ProfileStatus = silent ? "" : changed ? "Profil diperbarui (nama berubah)." : "Profil diperbarui.";
        }
        catch (CookieExpiredException) { item.SetPresence(AccountState.Expired, "Cookie kedaluwarsa"); if (_editing == item) ProfileStatus = "Cookie kedaluwarsa, deskripsi tidak bisa dibaca."; }
        catch (Exception ex) { Storage.Log("profile", ex); if (_editing == item && !silent) ProfileStatus = "Gagal: " + FriendlyError(ex); }
        finally { SetProfileBusy(false); }
    }

    private async Task SaveDescriptionAsync()
    {
        if (_editing is not { } item) return;
        if (!_profileLoaded) { ProfileStatus = "Ambil profil dulu agar deskripsi lama tidak tertimpa tanpa sengaja."; return; }
        if (_profileDescription.Length > 1000) { ProfileStatus = "Maksimal 1000 karakter."; return; }
        SetProfileBusy(true);
        ProfileStatus = "Menyimpan ke Roblox…";
        try
        {
            var cookie = Dpapi.Unprotect(item.Model.CookieProtected);
            var saved = await _api.SetDescriptionAsync(cookie, _profileDescription);
            ProfileStatus = saved != _profileDescription ? "Tersimpan, tapi sebagian teks difilter Roblox." : "Deskripsi tersimpan di Roblox.";
            if (_editing == item) ProfileDescription = saved;
        }
        catch (Exception ex) { Storage.Log("set description", ex); ProfileStatus = FriendlyError(ex); }
        finally { SetProfileBusy(false); }
    }

    private void EditDelete()
    {
        if (_editing is not { } item) return;
        if (!ConfirmDelete) { ConfirmDelete = true; return; }
        _accounts.Remove(item.Model);
        AllAccounts.Remove(item);
        SaveAccounts();
        OnSelectionChanged();
        RebuildGroups();
        RebuildVisibleAccounts();
        IsEditOpen = false;
        ShowMessage($"Akun {item.Name} dihapus.");
    }
}
