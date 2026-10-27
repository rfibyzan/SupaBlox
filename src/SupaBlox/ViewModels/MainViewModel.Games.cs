using System.Collections.ObjectModel;
using SupaBlox.Core;

namespace SupaBlox.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<GameItem> GameResults { get; } = new();
    public ObservableCollection<ServerItem> Servers { get; } = new();

    public RelayCommand RefreshServersCommand { get; private set; } = null!;
    public RelayCommand LoadMoreServersCommand { get; private set; } = null!;
    public RelayCommand ToggleFavoriteGameCommand { get; private set; } = null!;
    public RelayCommand UseInQuickCommand { get; private set; } = null!;
    public RelayCommand JoinRandomCommand { get; private set; } = null!;
    public RelayCommand CopyPlaceIdCommand { get; private set; } = null!;

    /// <summary>Diisi code-behind untuk akses clipboard.</summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    private void InitGames()
    {
        RefreshServersCommand = new RelayCommand(() => LoadServersAsync(reset: true));
        LoadMoreServersCommand = new RelayCommand(_ => LoadServersAsync(reset: false), _ => _serverCursor != null);
        ToggleFavoriteGameCommand = new RelayCommand(ToggleFavoriteGame);
        UseInQuickCommand = new RelayCommand(() =>
        {
            if (_selectedGame == null) return;
            SetQuickGame(_selectedGame.Model);
            ShowMessage($"{_selectedGame.Name} dipakai di Kontrol Cepat.");
        });
        JoinRandomCommand = new RelayCommand(() => JoinSelectedGame(null));
        CopyPlaceIdCommand = new RelayCommand(async () =>
        {
            if (_selectedGame != null && CopyToClipboard != null)
            {
                await CopyToClipboard(_selectedGame.PlaceIdText);
                ShowMessage("Place ID disalin.");
            }
        });
        ShowRecentAndFavorites();
    }

    private bool _gamesShownOnce;
    private void OnGamesPageShown()
    {
        if (_gamesShownOnce) return;
        _gamesShownOnce = true;
        _ = LoadGameIconsAsync(GameResults.ToList());
    }

    private string _gameQuery = "";
    private CancellationTokenSource? _gameCts;
    public string GameQuery
    {
        get => _gameQuery;
        set { if (Set(ref _gameQuery, value)) _ = RunGameSearchAsync(value); }
    }

    private string _gameListTitle = "Terakhir & favorit";
    public string GameListTitle { get => _gameListTitle; set => Set(ref _gameListTitle, value); }
    private string _gameListInfo = "";
    public string GameListInfo { get => _gameListInfo; set => Set(ref _gameListInfo, value); }

    private async Task RunGameSearchAsync(string text)
    {
        _gameCts?.Cancel();
        var cts = _gameCts = new CancellationTokenSource();
        text = text.Trim();
        if (text.Length < 2) { ShowRecentAndFavorites(); return; }
        try
        {
            await Task.Delay(350, cts.Token);
            GameListInfo = "mencari…";
            List<GameRef> found;
            if (Launcher.ParsePlaceId(text) is long placeId)
            {
                var g = await _api.ResolvePlaceAsync(placeId, cts.Token);
                found = g != null ? new() { g } : new();
            }
            else found = await _api.SearchGamesAsync(text, 20, cts.Token);
            if (cts.IsCancellationRequested) return;
            GameResults.Clear();
            foreach (var g in found) GameResults.Add(new GameItem(g, SelectGame, IsFavoriteGame(g.PlaceId)));
            GameListTitle = "Hasil pencarian";
            GameListInfo = $"{found.Count} game";
            await LoadGameIconsAsync(GameResults.ToList());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Storage.Log("game search", ex); GameListInfo = "gagal"; ShowMessage("Gagal mencari game: " + FriendlyError(ex), true); }
    }

    private void ShowRecentAndFavorites()
    {
        GameResults.Clear();
        var seen = new HashSet<long>();
        foreach (var g in _settings.FavoriteGames.Concat(_settings.RecentGames))
            if (seen.Add(g.PlaceId)) GameResults.Add(new GameItem(g, SelectGame, IsFavoriteGame(g.PlaceId)));
        GameListTitle = "Terakhir & favorit";
        GameListInfo = GameResults.Count == 0 ? "kosong, cari game di atas" : $"{GameResults.Count} game";
        if (_gamesShownOnce) _ = LoadGameIconsAsync(GameResults.ToList());
    }

    private bool IsFavoriteGame(long placeId) => _settings.FavoriteGames.Any(f => f.PlaceId == placeId);

    // ---------- Detail game ----------

    private GameItem? _selectedGame;
    public GameItem? SelectedGame
    {
        get => _selectedGame;
        private set { if (Set(ref _selectedGame, value)) Notify(nameof(HasSelectedGame), nameof(NoSelectedGame)); }
    }
    public bool HasSelectedGame => _selectedGame != null;
    public bool NoSelectedGame => _selectedGame == null;

    private void SelectGame(GameItem item)
    {
        foreach (var g in GameResults) g.IsCurrent = g == item;
        SelectedGame = item;
        _ = LoadServersAsync(reset: true);
    }

    private void ToggleFavoriteGame()
    {
        if (_selectedGame is not { } g) return;
        if (IsFavoriteGame(g.Model.PlaceId)) _settings.FavoriteGames.RemoveAll(f => f.PlaceId == g.Model.PlaceId);
        else _settings.FavoriteGames.Insert(0, g.Model);
        g.IsFavorite = IsFavoriteGame(g.Model.PlaceId);
        foreach (var r in GameResults.Where(r => r.Model.PlaceId == g.Model.PlaceId)) r.IsFavorite = g.IsFavorite;
        QueueSave();
    }

    // ---------- Server ----------

    private string? _serverCursor;
    private bool _loadingServers;
    public bool LoadingServers { get => _loadingServers; set => Set(ref _loadingServers, value); }
    private string _serverInfo = "";
    public string ServerInfo { get => _serverInfo; set => Set(ref _serverInfo, value); }
    public bool HasMoreServers => _serverCursor != null;

    private async Task LoadServersAsync(bool reset)
    {
        if (_selectedGame is not { } game || _loadingServers) return;
        LoadingServers = true;
        try
        {
            if (reset) { Servers.Clear(); _serverCursor = null; }
            ServerInfo = "memuat…";
            var (list, next) = await _api.GetServersAsync(game.Model.PlaceId, reset ? null : _serverCursor);
            if (_selectedGame != game) return;
            _serverCursor = next;
            var merged = Servers.Select(s => s.Info).Concat(list).GroupBy(s => s.JobId).Select(g => g.First())
                .OrderBy(s => s.Ping <= 0 ? int.MaxValue : s.Ping).ToList();
            Servers.Clear();
            foreach (var s in merged) Servers.Add(new ServerItem(s, JoinServer));
            ServerInfo = $"{Servers.Count} terlihat";
        }
        catch (Exception ex) { Storage.Log("servers", ex); ServerInfo = "gagal"; ShowMessage(FriendlyError(ex), true); }
        finally
        {
            LoadingServers = false;
            OnPropertyChanged(nameof(HasMoreServers));
            LoadMoreServersCommand.Refresh();
        }
    }

    private void JoinServer(ServerItem s) => JoinSelectedGame(s.JobId);

    private void JoinSelectedGame(string? jobId)
    {
        if (_selectedGame is not { } game) return;
        var accounts = SelectedAccounts;
        if (accounts.Count == 0) { ShowMessage("Centang akun di tab Akun dulu, lalu klik Join lagi.", true); return; }
        AddRecent(game.Model);
        QueueSave();
        var target = new LaunchTarget(game.Model.PlaceId, JobId: jobId);
        Enqueue(accounts.Select(a => (a, target)));
    }
}
