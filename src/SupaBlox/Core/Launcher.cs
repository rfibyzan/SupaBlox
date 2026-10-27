using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SupaBlox.Core;

/// <summary>Target join: server acak, JobId tertentu, atau server privat (linkCode).</summary>
public sealed record LaunchTarget(long PlaceId, string? JobId = null, string? PrivateLinkCode = null)
{
    public string Describe() => PrivateLinkCode != null ? "server privat" : JobId != null ? "server " + JobId[..Math.Min(8, JobId.Length)] + "…" : "server acak";
}

public static partial class Launcher
{
    public const string ProcessName = "RobloxPlayerBeta";

    /// <summary>Bangun URI roblox-player: seperti yang dipakai situs Roblox saat menekan Play.</summary>
    public static string BuildUri(string ticket, LaunchTarget t)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string tracker = Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();
        string attempt = Guid.NewGuid().ToString();
        string placeLauncher;
        if (t.PrivateLinkCode != null)
            placeLauncher = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&browserTrackerId={tracker}&placeId={t.PlaceId}&linkCode={t.PrivateLinkCode}&joinAttemptId={attempt}&joinAttemptOrigin=privateServerListJoin";
        else if (t.JobId != null)
            placeLauncher = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGameJob&browserTrackerId={tracker}&placeId={t.PlaceId}&gameId={t.JobId}&isPlayTogetherGame=false&joinAttemptId={attempt}&joinAttemptOrigin=publicServerListJoin";
        else
            placeLauncher = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={tracker}&placeId={t.PlaceId}&isPlayTogetherGame=false&joinAttemptId={attempt}&joinAttemptOrigin=PlayButton";

        return "roblox-player:1+launchmode:play+gameinfo:" + ticket +
               "+launchtime:" + now +
               "+placelauncherurl:" + Uri.EscapeDataString(placeLauncher) +
               "+browsertrackerid:" + tracker +
               "+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
    }

    public static void Start(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    public static HashSet<int> RunningPids()
    {
        var set = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName(ProcessName)) { set.Add(p.Id); p.Dispose(); }
        return set;
    }

    // ---------- Parser input ----------

    [GeneratedRegex(@"roblox\.com/games/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GamesUrl();

    [GeneratedRegex(@"privateServerLinkCode=([\w-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PrivateCode();

    [GeneratedRegex(@"roblox\.com/share\?code=([\w-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ShareCode();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex JobIdRx();

    [GeneratedRegex(@"^\d{4,}$")]
    private static partial Regex DigitsRx();

    /// <summary>"12345" atau URL roblox.com/games/12345/... -> placeId.</summary>
    public static long? ParsePlaceId(string text)
    {
        text = text.Trim();
        if (DigitsRx().IsMatch(text) && long.TryParse(text, out var id)) return id;
        var m = GamesUrl().Match(text);
        if (m.Success && long.TryParse(m.Groups[1].Value, out id)) return id;
        return null;
    }

    public static bool IsJobId(string text) => JobIdRx().IsMatch(text.Trim());

    /// <summary>Link lama: .../games/ID/x?privateServerLinkCode=CODE</summary>
    public static (long? PlaceId, string? Code) ParsePrivateLink(string text)
    {
        var c = PrivateCode().Match(text);
        if (!c.Success) return (null, null);
        return (ParsePlaceId(text), c.Groups[1].Value);
    }

    /// <summary>Link baru: roblox.com/share?code=CODE&amp;type=Server</summary>
    public static string? ParseShareCode(string text)
    {
        var m = ShareCode().Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }
}
