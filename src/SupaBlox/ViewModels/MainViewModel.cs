using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SupaBlox.Core;

namespace SupaBlox.ViewModels;

/// <summary>
/// Satu ViewModel untuk seluruh aplikasi. Halaman hanya disembunyikan (IsVisible), tidak dibuat ulang,
/// jadi isian tidak reset saat pindah tab. Pengaturan juga disimpan ke settings.json.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const string Version = "0.3.0";

    private readonly RobloxApi _api = new();
    private readonly AppSettings _settings;
    private readonly List<Account> _accounts;
    private readonly Dictionary<string, Bitmap> _imageCache = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _tick;

    public MainViewModel()
    {
        RelayCommand.ErrorSink = ex => { Storage.Log("command", ex); ShowMessage(FriendlyError(ex), true); };

        _settings = Storage.LoadSettings();
        _accounts = Storage.LoadAccounts();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); TrySave(() => Storage.SaveSettings(_settings)); };

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => OnTick();

        _pageIndex = Math.Clamp(_settings.LastPage, 0, 2);
        _startWithWindows = SafeGet(StartupRegistry.IsEnabled);

        InitCommands();
        InitAccounts();
        InitQuick();
        InitGames();
        if (_pageIndex == 1) OnGamesPageShown();

        _tick.Start();
        if (_settings.MultiOnStart && OperatingSystem.IsWindows()) MultiEnabled = true;
        RefreshInstances();
        _ = RefreshAllStatusAsync(silent: true);
    }

    // ---------- Navigasi ----------

    private int _pageIndex;
    public int PageIndex
    {
        get => _pageIndex;
        set
        {
            if (!Set(ref _pageIndex, value)) return;
            Notify(nameof(IsAccountsPage), nameof(IsGamesPage), nameof(IsInstancesPage));
            _settings.LastPage = value;
            QueueSave();
            if (value == 1) OnGamesPageShown();
        }
    }

    public bool IsAccountsPage => _pageIndex == 0;
    public bool IsGamesPage => _pageIndex == 1;
    public bool IsInstancesPage => _pageIndex == 2;
    public string Title => "SupaBlox v" + Version;

    // ---------- Status bar ----------

    private string _message = "";
    private bool _messageIsError;
    private int _messageToken;
    public string Message { get => _message; private set { if (Set(ref _message, value)) Notify(nameof(HasMessage)); } }
    public bool MessageIsError { get => _messageIsError; private set => Set(ref _messageIsError, value); }
    public bool HasMessage => _message.Length > 0;

    public void ShowMessage(string text, bool error = false)
    {
        Message = text;
        MessageIsError = error;
        int token = ++_messageToken;
        DispatcherTimer.RunOnce(() => { if (token == _messageToken) Message = ""; }, TimeSpan.FromSeconds(error ? 9 : 5));
    }

    public RelayCommand DismissMessageCommand { get; private set; } = null!;

    private static string FriendlyError(Exception ex) => ex switch
    {
        CookieExpiredException => "Cookie akun kedaluwarsa. Buka ⋯ lalu perbarui cookie.",
        HttpRequestException h => "Jaringan/Roblox error: " + h.Message,
        TaskCanceledException => "Permintaan ke Roblox timeout.",
        PlatformNotSupportedException p => p.Message,
        _ => ex.Message
    };

    // ---------- Simpan ----------

    public void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveAccounts() => TrySave(() => Storage.SaveAccounts(_accounts));

    public void FlushSave()
    {
        _saveTimer.Stop();
        TrySave(() => Storage.SaveSettings(_settings));
        SaveAccounts();
    }

    private void TrySave(Action a)
    {
        try { a(); }
        catch (Exception ex) { Storage.Log("save", ex); ShowMessage("Gagal menyimpan data: " + ex.Message, true); }
    }

    private static T SafeGet<T>(Func<T> f)
    {
        try { return f(); } catch { return default!; }
    }

    // ---------- Gambar ----------

    internal async Task<Bitmap?> LoadImageAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (_imageCache.TryGetValue(url, out var bmp)) return bmp;
        var bytes = await _api.DownloadAsync(url);
        if (bytes == null) return null;
        try
        {
            using var ms = new MemoryStream(bytes);
            bmp = new Bitmap(ms);
            _imageCache[url] = bmp;
            return bmp;
        }
        catch { return null; }
    }

    internal async Task LoadGameIconsAsync(IEnumerable<GameItem> items)
    {
        var list = items.Where(i => i.Icon == null).ToList();
        if (list.Count == 0) return;
        try
        {
            var missing = list.Where(i => i.Model.UniverseId > 0 && string.IsNullOrEmpty(i.Model.IconUrl)).Select(i => i.Model.UniverseId).ToList();
            if (missing.Count > 0)
            {
                var urls = await _api.GetGameIconsAsync(missing);
                foreach (var i in list)
                    if (urls.TryGetValue(i.Model.UniverseId, out var u)) i.Model.IconUrl = u;
            }
            foreach (var i in list) i.Icon = await LoadImageAsync(i.Model.IconUrl);
        }
        catch (Exception ex) { Storage.Log("icons", ex); }
    }

    // ---------- Tick 1 dtk ----------

    private int _tickCount;
    private void OnTick()
    {
        _tickCount++;
        UpdateUptime();
        if (_tickCount % 2 == 0) RefreshInstances();
    }

    public void Shutdown()
    {
        FlushSave();
        _holder?.Dispose();
        _holder = null;
    }
}
