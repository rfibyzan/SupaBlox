using System.Text.Json.Serialization;

namespace SupaBlox.Core;

public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Alias { get; set; } = "";
    public string Group { get; set; } = "";
    public long? DefaultPlaceId { get; set; }
    public string DefaultPlaceName { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Favorite { get; set; }
    /// <summary>.ROBLOSECURITY terenkripsi DPAPI (base64). Tidak pernah disimpan polos.</summary>
    public string CookieProtected { get; set; } = "";
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class GameRef
{
    public long PlaceId { get; set; }
    public long UniverseId { get; set; }
    public string Name { get; set; } = "";
    public string Creator { get; set; } = "";
    public long Playing { get; set; }
    public string? IconUrl { get; set; }
}

public sealed class AppSettings
{
    // Kontrol Cepat (persist antar tab dan setelah restart)
    public GameRef? QuickGame { get; set; }
    public string QuickJobId { get; set; } = "";
    public int LaunchDelaySec { get; set; } = 8;
    public int MaxInstances { get; set; } = 6;
    public bool UseDefaultGame { get; set; } = true;
    public bool HideUsernames { get; set; }
    public List<string> SelectedAccountIds { get; set; } = new();
    public List<GameRef> RecentGames { get; set; } = new();
    public List<GameRef> FavoriteGames { get; set; } = new();

    // Aplikasi
    public bool MinimizeToTray { get; set; } = true;
    public bool MultiOnStart { get; set; } = true;
    public int LastPage { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<Account>))]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppJson : JsonSerializerContext { }
