using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using SupaBlox.Core;

namespace SupaBlox.ViewModels;

public sealed class CommandItem : ObservableObject
{
    public CommandItem(string icon, string title, string subtitle, string kind, Action run)
    {
        Icon = icon; Title = title; Subtitle = subtitle; Kind = kind; Run = run;
    }
    public string Icon { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Kind { get; }
    public Action Run { get; }
    public Bitmap? Image { get; init; }
    public bool HasImage => Image != null;
    public bool NoImage => Image == null;

    private bool _current;
    public bool IsCurrent { get => _current; set => Set(ref _current, value); }
}

/// <summary>Search global Ctrl+K: akun, game, Job ID, follow username, dan aksi cepat.</summary>
public sealed partial class MainViewModel
{
    public ObservableCollection<CommandItem> PaletteItems { get; } = new();

    private bool _isPaletteOpen;
    public bool IsPaletteOpen { get => _isPaletteOpen; set => Set(ref _isPaletteOpen, value); }

    private string _paletteQuery = "";
    public string PaletteQuery { get => _paletteQuery; set { if (Set(ref _paletteQuery, value)) BuildPalette(); } }
    public bool PaletteEmpty => PaletteItems.Count == 0;

    private int _paletteIndex;

    private RelayCommand? _openPalette;
    public RelayCommand OpenPaletteCommand => _openPalette ??= new RelayCommand(OpenPalette);

    public void OpenPalette()
    {
        _paletteQuery = "";
        OnPropertyChanged(nameof(PaletteQuery));
        BuildPalette();
        IsPaletteOpen = true;
    }

    public void ClosePalette() => IsPaletteOpen = false;

    public void MovePalette(int delta)
    {
        if (PaletteItems.Count == 0) return;
        _paletteIndex = (_paletteIndex + delta + PaletteItems.Count) % PaletteItems.Count;
        for (int i = 0; i < PaletteItems.Count; i++) PaletteItems[i].IsCurrent = i == _paletteIndex;
    }

    public void RunPalette(CommandItem? item = null)
    {
        item ??= _paletteIndex < PaletteItems.Count ? PaletteItems[_paletteIndex] : null;
        if (item == null) return;
        IsPaletteOpen = false;
        try { item.Run(); }
        catch (Exception ex) { Storage.Log("palette", ex); ShowMessage(FriendlyError(ex), true); }
    }

    private static readonly Regex UsernameRx = new(@"^@?[A-Za-z0-9_]{3,20}$");

    private void BuildPalette()
    {
        var q = _paletteQuery.Trim();
        var items = new List<CommandItem>();

        bool Match(string s) => q.Length == 0 || s.Contains(q, StringComparison.OrdinalIgnoreCase);

        // Input khusus dulu
        if (Launcher.IsJobId(q))
            items.Add(new CommandItem("⚡", "Pakai Job ID di Kontrol Cepat", q, "Server", () => { QuickJobId = q; PageIndex = 0; ShowMessage("Job ID dipasang di Kontrol Cepat."); }));
        else if (Launcher.ParseShareCode(q) != null || Launcher.ParsePrivateLink(q).Code != null)
            items.Add(new CommandItem("⚡", "Pakai link server privat di Kontrol Cepat", q, "Server", () => { QuickJobId = q; PageIndex = 0; }));
        else if (Launcher.ParsePlaceId(q) is long pid)
            items.Add(new CommandItem("◈", $"Buka Place ID {pid}", "Game & server", "Game", () => { PageIndex = 1; GameQuery = q; }));

        // Akun
        if (q.Length > 0)
            foreach (var a in AllAccounts.Where(a => Match(a.Name) || Match(a.Model.Username) || Match(a.Model.Note) || Match(a.Model.Group)).Take(5))
            {
                var acc = a;
                items.Add(new CommandItem(acc.Initial, acc.Name, $"{acc.UserText}  •  {acc.GroupText}  •  {acc.StatusText}", "Akun",
                    () => { PageIndex = 0; GroupFilter = AllGroupsName; AccountFilter = acc.Model.Username; }) { Image = acc.Avatar });
            }

        // Game terakhir & favorit
        var games = _settings.FavoriteGames.Concat(_settings.RecentGames).GroupBy(g => g.PlaceId).Select(g => g.First());
        foreach (var g in games.Where(g => Match(g.Name)).Take(q.Length == 0 ? 3 : 4))
        {
            var game = g;
            items.Add(new CommandItem("◈", game.Name, $"Place ID {game.PlaceId}", "Game", () => OpenGameFromPalette(game)));
        }
        if (q.Length >= 2 && Launcher.ParsePlaceId(q) == null && !Launcher.IsJobId(q))
            items.Add(new CommandItem("⌕", $"Cari game “{q}”", "Game & server", "Game", () => { PageIndex = 1; GameQuery = q; }));

        // Follow
        if (UsernameRx.IsMatch(q) && !Launcher.IsJobId(q))
        {
            var u = q.TrimStart('@');
            items.Add(new CommandItem("➜", $"Follow @{u}", SelectedCount > 0 ? $"{SelectedCount} akun terpilih ikut join" : "centang akun dulu di tab Akun", "Follow",
                () => { FollowUsername = u; _ = FollowAsync(u); }));
        }

        // Aksi
        var actions = new List<CommandItem>
        {
            new("+", "Tambah akun", "login browser / cookie", "Aksi", () => { PageIndex = 0; OpenAddCommand.Execute(null); }),
            new("⟳", "Refresh status semua akun", "presence", "Aksi", () => RefreshStatusCommand.Execute(null)),
            new("⇩", "Ekspor akun", "file .sbx terenkripsi", "Aksi", () => OpenExportCommand.Execute(null)),
            new("⇧", "Impor akun dari file", "file .sbx", "Aksi", () => OpenImportCommand.Execute(null)),
            new("▣", MultiEnabled ? "Matikan Multi Roblox" : "Nyalakan Multi Roblox", "mutex singleton", "Aksi", () => MultiEnabled = !MultiEnabled),
            new("☺", "Buka Akun", "halaman", "Halaman", () => PageIndex = 0),
            new("◈", "Buka Game & server", "halaman", "Halaman", () => PageIndex = 1),
            new("▭", "Buka Instance", "halaman", "Halaman", () => PageIndex = 2),
        };
        items.AddRange(actions.Where(a => Match(a.Title)));

        PaletteItems.Clear();
        foreach (var i in items.Take(12)) PaletteItems.Add(i);
        _paletteIndex = 0;
        if (PaletteItems.Count > 0) PaletteItems[0].IsCurrent = true;
        Notify(nameof(PaletteEmpty));
    }

    private void OpenGameFromPalette(GameRef g)
    {
        PageIndex = 1;
        var existing = GameResults.FirstOrDefault(x => x.Model.PlaceId == g.PlaceId);
        var item = existing ?? new GameItem(g, SelectGame, IsFavoriteGame(g.PlaceId));
        if (existing == null) _ = LoadGameIconsAsync(new[] { item });
        SelectGame(item);
    }
}
