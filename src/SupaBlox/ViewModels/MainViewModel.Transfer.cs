using System.Security.Cryptography;
using SupaBlox.Core;

namespace SupaBlox.ViewModels;

/// <summary>Ekspor/impor akun terenkripsi password (.sbx) untuk pindah PC.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Diisi code-behind: dialog simpan/buka file. Mengembalikan path lokal atau null.</summary>
    public Func<string, Task<string?>>? PickSaveFile { get; set; }
    public Func<Task<string?>>? PickOpenFile { get; set; }

    private bool _isTransferOpen, _transferBusy, _transferOnlySelected;
    private int _transferMode; // 0 ekspor, 1 impor
    private string _transferPass = "", _transferPass2 = "", _transferStatus = "", _transferFile = "";

    public bool IsTransferOpen { get => _isTransferOpen; set => Set(ref _isTransferOpen, value); }
    public bool IsExportMode => _transferMode == 0;
    public bool IsImportMode => _transferMode == 1;
    public string TransferTitle => IsExportMode ? "Ekspor akun" : "Impor akun";
    public string TransferSubtitle => IsExportMode
        ? "File .sbx dienkripsi password (AES-256). Bisa dibuka di PC lain."
        : "Buka file .sbx dari SupaBlox lain. Akun yang sudah ada diperbarui cookie-nya.";
    public string TransferRunText => IsExportMode ? "Simpan file…" : "Impor";
    public string TransferPassword { get => _transferPass; set => Set(ref _transferPass, value); }
    public string TransferPassword2 { get => _transferPass2; set => Set(ref _transferPass2, value); }
    public bool TransferOnlySelected { get => _transferOnlySelected; set => Set(ref _transferOnlySelected, value); }
    public string TransferSelectedText => $"Hanya akun yang dicentang ({SelectedCount})";
    public string TransferStatus { get => _transferStatus; set { if (Set(ref _transferStatus, value)) Notify(nameof(HasTransferStatus)); } }
    public bool HasTransferStatus => _transferStatus.Length > 0;
    public string TransferFile { get => _transferFile; set { if (Set(ref _transferFile, value)) Notify(nameof(TransferFileText)); } }
    public string TransferFileText => _transferFile.Length == 0 ? "Belum ada file dipilih" : Path.GetFileName(_transferFile);
    public bool TransferIdle => !_transferBusy;

    private RelayCommand? _openExport, _openImport, _closeTransfer, _chooseFile, _runTransfer;
    public RelayCommand OpenExportCommand => _openExport ??= new RelayCommand(() => OpenTransfer(0));
    public RelayCommand OpenImportCommand => _openImport ??= new RelayCommand(() => OpenTransfer(1));
    public RelayCommand CloseTransferCommand => _closeTransfer ??= new RelayCommand(() => { if (!_transferBusy) IsTransferOpen = false; });
    public RelayCommand TransferChooseFileCommand => _chooseFile ??= new RelayCommand(async () =>
    {
        if (PickOpenFile == null) return;
        var path = await PickOpenFile();
        if (path != null) { TransferFile = path; TransferStatus = ""; }
    });
    public RelayCommand TransferRunCommand => _runTransfer ??= new RelayCommand(() => IsExportMode ? ExportAsync() : ImportAsync());

    private void OpenTransfer(int mode)
    {
        _transferMode = mode;
        TransferPassword = TransferPassword2 = TransferStatus = TransferFile = "";
        TransferOnlySelected = mode == 0 && SelectedCount > 0;
        Notify(nameof(IsExportMode), nameof(IsImportMode), nameof(TransferTitle), nameof(TransferSubtitle), nameof(TransferRunText), nameof(TransferSelectedText));
        IsTransferOpen = true;
    }

    private void SetTransferBusy(bool v) { _transferBusy = v; Notify(nameof(TransferIdle)); }

    private async Task ExportAsync()
    {
        if (_transferPass.Length < 6) { TransferStatus = "Password minimal 6 karakter."; return; }
        if (_transferPass != _transferPass2) { TransferStatus = "Konfirmasi password tidak sama."; return; }
        var list = _transferOnlySelected ? SelectedAccounts : AllAccounts.ToList();
        if (list.Count == 0) { TransferStatus = "Tidak ada akun untuk diekspor."; return; }
        if (PickSaveFile == null) return;
        var path = await PickSaveFile($"SupaBlox-akun-{DateTime.Now:yyyyMMdd}.sbx");
        if (path == null) return;

        SetTransferBusy(true);
        TransferStatus = "Mengenkripsi…";
        try
        {
            var payload = new ExportPayload { Groups = AllGroupNames() };
            int skipped = 0;
            foreach (var a in list)
            {
                string cookie;
                try { cookie = Dpapi.Unprotect(a.Model.CookieProtected); }
                catch { skipped++; continue; }
                var m = a.Model;
                payload.Accounts.Add(new ExportAccount
                {
                    UserId = m.UserId, Username = m.Username, DisplayName = m.DisplayName, Alias = m.Alias, Group = m.Group,
                    DefaultPlaceId = m.DefaultPlaceId, DefaultPlaceName = m.DefaultPlaceName, Note = m.Note, Favorite = m.Favorite,
                    Cookie = cookie, Watch = m.Watch, AutoRelaunch = m.AutoRelaunch, RelaunchDelaySec = m.RelaunchDelaySec,
                    MaxRelaunch = m.MaxRelaunch, RelaunchOnNormalClose = m.RelaunchOnNormalClose, RejoinSameServer = m.RejoinSameServer,
                });
            }
            var pass = _transferPass;
            var json = await Task.Run(() => ExportCrypto.Encrypt(payload, pass));
            await File.WriteAllTextAsync(path, json);
            foreach (var e in payload.Accounts) e.Cookie = "";
            IsTransferOpen = false;
            ShowMessage($"{payload.Accounts.Count} akun diekspor ke {Path.GetFileName(path)}." + (skipped > 0 ? $" {skipped} dilewati (cookie tidak bisa dibuka)." : ""), skipped > 0);
        }
        catch (Exception ex) { Storage.Log("export", ex); TransferStatus = "Gagal ekspor: " + ex.Message; }
        finally { SetTransferBusy(false); TransferPassword = TransferPassword2 = ""; }
    }

    private async Task ImportAsync()
    {
        if (_transferFile.Length == 0) { TransferStatus = "Pilih file .sbx dulu."; return; }
        if (_transferPass.Length == 0) { TransferStatus = "Isi password file."; return; }
        SetTransferBusy(true);
        TransferStatus = "Mendekripsi…";
        try
        {
            var json = await File.ReadAllTextAsync(_transferFile);
            var pass = _transferPass;
            ExportPayload payload;
            try { payload = await Task.Run(() => ExportCrypto.Decrypt(json, pass)); }
            catch (CryptographicException) { TransferStatus = "Password salah atau file rusak."; return; }

            int added = 0, updated = 0;
            foreach (var e in payload.Accounts)
            {
                if (e.UserId == 0 || string.IsNullOrEmpty(e.Cookie)) continue;
                var existing = AllAccounts.FirstOrDefault(a => a.Model.UserId == e.UserId);
                if (existing != null)
                {
                    var m = existing.Model;
                    m.CookieProtected = Dpapi.Protect(e.Cookie);
                    if (m.Alias.Length == 0) m.Alias = e.Alias;
                    if (m.Group.Length == 0) m.Group = e.Group;
                    if (m.Note.Length == 0) m.Note = e.Note;
                    if (m.DefaultPlaceId == null) { m.DefaultPlaceId = e.DefaultPlaceId; m.DefaultPlaceName = e.DefaultPlaceName; }
                    existing.SetPresence(AccountState.Offline, "Cookie diimpor");
                    existing.RefreshTexts();
                    updated++;
                }
                else
                {
                    var acc = new Account
                    {
                        UserId = e.UserId, Username = e.Username, DisplayName = e.DisplayName, Alias = e.Alias, Group = e.Group,
                        DefaultPlaceId = e.DefaultPlaceId, DefaultPlaceName = e.DefaultPlaceName, Note = e.Note, Favorite = e.Favorite,
                        CookieProtected = Dpapi.Protect(e.Cookie), Watch = e.Watch, AutoRelaunch = e.AutoRelaunch,
                        RelaunchDelaySec = e.RelaunchDelaySec, MaxRelaunch = e.MaxRelaunch,
                        RelaunchOnNormalClose = e.RelaunchOnNormalClose, RejoinSameServer = e.RejoinSameServer,
                    };
                    _accounts.Add(acc);
                    var item = new AccountItem(this, acc);
                    item.SetPresence(AccountState.Offline, "Diimpor");
                    AllAccounts.Add(item);
                    added++;
                }
                e.Cookie = "";
            }
            foreach (var g in payload.Groups)
                if (!string.IsNullOrWhiteSpace(g) && !_settings.Groups.Contains(g)) _settings.Groups.Add(g);
            SaveAccounts();
            QueueSave();
            RebuildGroups();
            RebuildVisibleAccounts();
            _ = LoadAvatarsAsync(AllAccounts.ToList());
            _ = RefreshAllStatusAsync(silent: true);
            IsTransferOpen = false;
            ShowMessage($"Impor selesai: {added} akun baru, {updated} diperbarui.");
        }
        catch (Exception ex) { Storage.Log("import", ex); TransferStatus = "Gagal impor: " + ex.Message; }
        finally { SetTransferBusy(false); TransferPassword = ""; }
    }
}
