using Avalonia.Media;
using Avalonia.Media.Imaging;
using SupaBlox.Core;

namespace SupaBlox.ViewModels;

public static class Palette
{
    public static readonly IBrush Green = new SolidColorBrush(Color.Parse("#34D399"));
    public static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#FBBF24"));
    public static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F87171"));
    public static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#5F6880"));
    public static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#60A5FA"));

    private static readonly string[] Avatars = { "#6366F1", "#EC4899", "#14B8A6", "#F59E0B", "#3B82F6", "#A855F7", "#EF4444", "#10B981" };

    public static IBrush ForKey(string key)
    {
        int h = 0;
        foreach (var c in key) h = unchecked(h * 31 + c);
        return new SolidColorBrush(Color.Parse(Avatars[Math.Abs(h % Avatars.Length)]));
    }

    public static string Initial(string name)
    {
        foreach (var c in name) if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        return "?";
    }

    public static string Count(long n) =>
        n >= 1_000_000 ? (n / 1_000_000d).ToString("0.#") + "M" :
        n >= 1_000 ? (n / 1_000d).ToString("0.#") + "K" : n.ToString();
}

public enum AccountState { Unknown, Offline, Online, InGame, Studio, Expired, Running, Launching }

public sealed class AccountItem : ObservableObject
{
    private readonly MainViewModel _vm;

    public AccountItem(MainViewModel vm, Account model)
    {
        _vm = vm;
        Model = model;
        AvatarBrush = Palette.ForKey(model.Username);
        LaunchCommand = new RelayCommand(() => _vm.EnqueueQuick(new[] { this }));
        FavoriteCommand = new RelayCommand(() => { Model.Favorite = !Model.Favorite; _vm.SaveAccounts(); Notify(nameof(FavoriteGlyph), nameof(FavoriteBrush)); _vm.RebuildVisibleAccounts(); });
        EditCommand = new RelayCommand(() => _vm.OpenEdit(this));
    }

    public Account Model { get; }
    public RelayCommand LaunchCommand { get; }
    public RelayCommand FavoriteCommand { get; }
    public RelayCommand EditCommand { get; }

    public string Name => string.IsNullOrWhiteSpace(Model.Alias) ? (string.IsNullOrWhiteSpace(Model.DisplayName) ? Model.Username : Model.DisplayName) : Model.Alias;
    public string UserText => _vm.HideUsernames ? "@••••••" : "@" + Model.Username;
    public string Initial => Palette.Initial(Name);
    public IBrush AvatarBrush { get; }
    public string GroupText => string.IsNullOrWhiteSpace(Model.Group) ? "—" : Model.Group;
    public string DefaultGameText => Model.DefaultPlaceId is null ? "—" : (string.IsNullOrWhiteSpace(Model.DefaultPlaceName) ? Model.DefaultPlaceId.ToString()! : Model.DefaultPlaceName);
    public string FavoriteGlyph => Model.Favorite ? "★" : "☆";
    public IBrush FavoriteBrush => Model.Favorite ? Palette.Yellow : Palette.Dim;

    private Bitmap? _avatar;
    public Bitmap? Avatar { get => _avatar; set => Set(ref _avatar, value); }

    private bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) _vm.OnSelectionChanged(); }
    }

    public void SetSelectedSilently(bool v) { _selected = v; OnPropertyChanged(nameof(IsSelected)); }

    private AccountState _state = AccountState.Unknown;
    public AccountState State { get => _state; private set => Set(ref _state, value); }

    private string _presenceText = "Status belum dicek";
    private AccountState _presenceState = AccountState.Unknown;
    private int? _pid;
    private bool _launching;

    public int? RunningPid => _pid;
    public bool IsRunning => _pid != null;
    public bool CanLaunch => _pid == null && !_launching;
    public string RunningText => _pid != null ? $"● Berjalan • PID {_pid}" : "Membuka…";
    public bool ShowPill => _pid != null || _launching;

    public string StatusText => _presenceState == AccountState.Expired ? "Cookie kedaluwarsa" :
                                _pid != null && _presenceState != AccountState.InGame ? "Roblox berjalan" : _presenceText;

    public IBrush StatusBrush => _presenceState switch
    {
        AccountState.Expired => Palette.Red,
        AccountState.InGame => Palette.Green,
        AccountState.Online => Palette.Blue,
        AccountState.Studio => Palette.Yellow,
        _ => _pid != null ? Palette.Green : Palette.Dim
    };

    public void SetPresence(AccountState s, string text)
    {
        _presenceState = s;
        _presenceText = text;
        State = s;
        Notify(nameof(StatusText), nameof(StatusBrush));
    }

    public void SetPid(int? pid)
    {
        if (_pid == pid) return;
        _pid = pid;
        if (pid != null) _launching = false;
        Notify(nameof(RunningPid), nameof(IsRunning), nameof(CanLaunch), nameof(RunningText), nameof(ShowPill), nameof(StatusText), nameof(StatusBrush));
    }

    public void SetLaunching(bool v)
    {
        _launching = v;
        Notify(nameof(CanLaunch), nameof(RunningText), nameof(ShowPill));
    }

    public bool IsExpired => _presenceState == AccountState.Expired;

    public void RefreshTexts() => Notify(nameof(Name), nameof(UserText), nameof(Initial), nameof(GroupText), nameof(DefaultGameText), nameof(FavoriteGlyph), nameof(FavoriteBrush));
}

public sealed class GameItem : ObservableObject
{
    public GameItem(GameRef model, Action<GameItem> onSelect, bool isFavorite = false)
    {
        Model = model;
        AvatarBrush = Palette.ForKey(model.Name);
        SelectCommand = new RelayCommand(() => onSelect(this));
        _fav = isFavorite;
    }

    public GameRef Model { get; }
    public RelayCommand SelectCommand { get; }
    public string Name => Model.Name;
    public string ShortName => Model.Name.Length > 18 ? Model.Name[..17].TrimEnd() + "…" : Model.Name;
    public string CreatorText => string.IsNullOrWhiteSpace(Model.Creator) ? "Game" : Model.Creator + " • Game";
    public string PlayingText => Palette.Count(Model.Playing) + " bermain";
    public string PlaceIdText => Model.PlaceId.ToString();
    public string Initial => Palette.Initial(Model.Name);
    public IBrush AvatarBrush { get; }

    private Bitmap? _icon;
    public Bitmap? Icon { get => _icon; set => Set(ref _icon, value); }

    private bool _fav;
    public bool IsFavorite { get => _fav; set { if (Set(ref _fav, value)) Notify(nameof(FavoriteGlyph), nameof(FavoriteBrush)); } }
    public string FavoriteGlyph => _fav ? "★" : "☆";
    public IBrush FavoriteBrush => _fav ? Palette.Yellow : Palette.Dim;

    private bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
}

public sealed class ServerItem : ObservableObject
{
    public ServerItem(ServerInfo s, Action<ServerItem> onJoin, Action<ServerItem> onCopy, Action<ServerItem> onUse)
    {
        Info = s;
        JoinCommand = new RelayCommand(() => onJoin(this));
        CopyCommand = new RelayCommand(() => onCopy(this));
        UseCommand = new RelayCommand(() => onUse(this));
    }

    public ServerInfo Info { get; }
    public RelayCommand JoinCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand UseCommand { get; }
    public string JobId => Info.JobId;
    public string PingText => Info.Ping > 0 ? $"{Info.Ping} ms" : "? ms";
    public IBrush PingBrush => Info.Ping <= 0 ? Palette.Dim : Info.Ping < 60 ? Palette.Green : Info.Ping < 120 ? Palette.Yellow : Palette.Red;
    public string PlayersText => $"{Info.Playing} / {Info.MaxPlayers}";
    public double Fill => Info.MaxPlayers > 0 ? 100.0 * Info.Playing / Info.MaxPlayers : 0;

    /// <summary>null = belum dicek.</summary>
    private string? _region;
    public string? Region { get => _region; set { if (Set(ref _region, value)) Notify(nameof(RegionText)); } }
    public string RegionText => _region ?? "region…";
}

public sealed class InstanceItem : ObservableObject
{
    public InstanceItem(int pid, MainViewModel vm)
    {
        Pid = pid;
        FocusCommand = new RelayCommand(() => vm.FocusInstance(this));
        CloseCommand = new RelayCommand(() => vm.CloseInstance(this));
    }

    public int Pid { get; }
    public RelayCommand FocusCommand { get; }
    public RelayCommand CloseCommand { get; }

    private string _account = "—";
    public string AccountName { get => _account; set { if (Set(ref _account, value)) Notify(nameof(Initial)); } }
    public string Initial => _account == "—" ? "?" : Palette.Initial(_account);

    private IBrush _brush = Palette.Dim;
    public IBrush AvatarBrush { get => _brush; set => Set(ref _brush, value); }

    public string StartText { get; set; } = "—";
    public DateTime? StartTime { get; set; }

    private string _uptime = "—";
    /// <summary>Lama proses berjalan, format jj:mm:dd.</summary>
    public string UptimeText { get => _uptime; private set => Set(ref _uptime, value); }
    public void UpdateUptime()
    {
        if (StartTime is not { } t) { UptimeText = "—"; return; }
        var d = DateTime.Now - t;
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        UptimeText = $"{(int)d.TotalHours:00}:{d.Minutes:00}:{d.Seconds:00}";
    }

    private string _ram = "";
    public string RamText { get => _ram; set => Set(ref _ram, value); }

    private string _title = "";
    public string Title { get => _title; set => Set(ref _title, value); }

    /// <summary>Dipilih untuk tetap jalan saat Multi Roblox dimatikan.</summary>
    private bool _keep;
    public bool Keep { get => _keep; set => Set(ref _keep, value); }
}

public sealed class QueueItem : ObservableObject
{
    public QueueItem(AccountItem account, LaunchTarget target, Action<QueueItem> onCancel)
    {
        Account = account;
        Target = target;
        CancelCommand = new RelayCommand(() => onCancel(this));
    }

    public AccountItem Account { get; }
    public LaunchTarget Target { get; }
    public RelayCommand CancelCommand { get; }
    public string Name => Account.Name;
    public string Initial => Account.Initial;
    public IBrush AvatarBrush => Account.AvatarBrush;

    private string _status = "antre";
    public string Status { get => _status; set => Set(ref _status, value); }

    private IBrush _statusBrush = Palette.Dim;
    public IBrush StatusBrush { get => _statusBrush; set => Set(ref _statusBrush, value); }
}
