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

    // Watch + auto-relaunch (hanya memantau proses lokal, tidak menyentuh memori Roblox)
    public bool Watch { get; set; }
    public bool AutoRelaunch { get; set; }
    public int RelaunchDelaySec { get; set; } = 10;
    public int MaxRelaunch { get; set; } = 3;
    public bool RelaunchOnNormalClose { get; set; }
    public bool RejoinSameServer { get; set; } = true;

    // Profil publik terakhir yang diambil
    public DateTime? CreatedUtc { get; set; }
}

/// <summary>Isi file ekspor (.sbx) setelah didekripsi. Cookie polos hanya ada di memori.</summary>
public sealed class ExportAccount
{
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Alias { get; set; } = "";
    public string Group { get; set; } = "";
    public long? DefaultPlaceId { get; set; }
    public string DefaultPlaceName { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Favorite { get; set; }
    public string Cookie { get; set; } = "";
    public bool Watch { get; set; }
    public bool AutoRelaunch { get; set; }
    public int RelaunchDelaySec { get; set; } = 10;
    public int MaxRelaunch { get; set; } = 3;
    public bool RelaunchOnNormalClose { get; set; }
    public bool RejoinSameServer { get; set; } = true;
}

public sealed class ExportPayload
{
    public string App { get; set; } = "SupaBlox";
    public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
    public List<string> Groups { get; set; } = new();
    public List<ExportAccount> Accounts { get; set; } = new();
}

/// <summary>File .sbx: AES-256-GCM, kunci dari password via PBKDF2-SHA256.</summary>
public sealed class ExportFile
{
    public string Format { get; set; } = "supablox-export";
    public int Version { get; set; } = 1;
    public int Iterations { get; set; }
    public string Salt { get; set; } = "";
    public string Nonce { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Data { get; set; } = "";
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
    public string FollowUsername { get; set; } = "";
    /// <summary>Grup yang dibuat manual (tetap ada walau belum ada akunnya).</summary>
    public List<string> Groups { get; set; } = new();

    // Aplikasi
    public bool MinimizeToTray { get; set; } = true;
    public bool MultiOnStart { get; set; } = true;
    public int LastPage { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<Account>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ExportPayload))]
[JsonSerializable(typeof(ExportFile))]
internal partial class AppJson : JsonSerializerContext { }
