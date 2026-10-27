using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SupaBlox.Core;

public sealed record RobloxUser(long Id, string Name, string DisplayName);
public sealed record ServerInfo(string JobId, int Playing, int MaxPlayers, int Ping);
public sealed record PresenceInfo(long UserId, int Type, string Location, long? PlaceId, string? GameId);

public sealed class CookieExpiredException : Exception
{
    public CookieExpiredException() : base("Cookie kedaluwarsa atau tidak valid.") { }
}

/// <summary>
/// Klien Web API resmi Roblox (*.roblox.com). Hanya HTTP, tidak menyentuh proses Roblox.
/// </summary>
public sealed class RobloxApi
{
    private readonly HttpClient _http;

    public RobloxApi()
    {
        var handler = new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SupaBlox/0.2");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ---------- Akun ----------

    public async Task<RobloxUser> GetAuthenticatedUserAsync(string cookie, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
        AddCookie(req, cookie);
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new CookieExpiredException();
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;
        return new RobloxUser(r.GetProperty("id").GetInt64(), Str(r, "name"), Str(r, "displayName"));
    }

    /// <summary>Username -> user (tanpa cookie). null jika tidak ada.</summary>
    public async Task<RobloxUser?> GetUserByUsernameAsync(string username, CancellationToken ct = default)
    {
        var body = "{\"usernames\":[" + JsonSerializer.Serialize(username.Trim().TrimStart('@'), PresenceJson.Default.String) + "],\"excludeBannedUsers\":false}";
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://users.roblox.com/v1/usernames/users")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        foreach (var u in doc.RootElement.GetProperty("data").EnumerateArray())
            return new RobloxUser(u.GetProperty("id").GetInt64(), Str(u, "name"), Str(u, "displayName"));
        return null;
    }

    /// <summary>Profil publik: nama, display name, tanggal dibuat.</summary>
    public async Task<(RobloxUser User, DateTime? Created)> GetUserInfoAsync(long userId, CancellationToken ct = default)
    {
        using var res = await _http.GetAsync($"https://users.roblox.com/v1/users/{userId}", ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;
        DateTime? created = r.TryGetProperty("created", out var c) && c.ValueKind == JsonValueKind.String && c.TryGetDateTime(out var d) ? d.ToUniversalTime() : null;
        return (new RobloxUser(userId, Str(r, "name"), Str(r, "displayName")), created);
    }

    /// <summary>Deskripsi profil akun sendiri (users.roblox.com/v1/description).</summary>
    public async Task<string> GetDescriptionAsync(string cookie, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://users.roblox.com/v1/description");
        AddCookie(req, cookie);
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new CookieExpiredException();
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return Str(doc.RootElement, "description");
    }

    /// <summary>Ubah deskripsi profil. Mengembalikan teks yang tersimpan (bisa difilter Roblox jadi ###).</summary>
    public async Task<string> SetDescriptionAsync(string cookie, string description, CancellationToken ct = default)
    {
        var body = "{\"description\":" + JsonSerializer.Serialize(description, PresenceJson.Default.String) + "}";
        using var res = await SendWithCsrfAsync(HttpMethod.Post, "https://users.roblox.com/v1/description", cookie, body, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new CookieExpiredException();
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Roblox menolak (HTTP {(int)res.StatusCode}): {RobloxError(text)}");
        try { using var doc = JsonDocument.Parse(text); return Str(doc.RootElement, "description"); }
        catch { return description; }
    }

    private static string RobloxError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("errors", out var errs))
                foreach (var e in errs.EnumerateArray()) return Str(e, "message");
        }
        catch { }
        return json.Length > 160 ? json[..160] : json;
    }

    /// <summary>Tiket sekali pakai untuk roblox-player: (sama seperti tombol Play di web).</summary>
    public async Task<string> GetAuthTicketAsync(string cookie, CancellationToken ct = default)
    {
        using var res = await SendWithCsrfAsync(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket", cookie, "{}", ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new CookieExpiredException();
        if (res.Headers.TryGetValues("rbx-authentication-ticket", out var vals))
        {
            var t = vals.FirstOrDefault();
            if (!string.IsNullOrEmpty(t)) return t;
        }
        throw new InvalidOperationException($"Gagal mengambil auth ticket (HTTP {(int)res.StatusCode}).");
    }

    public async Task<List<PresenceInfo>> GetPresenceAsync(string cookie, IEnumerable<long> userIds, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, long[]> { ["userIds"] = userIds.ToArray() }, PresenceJson.Default.DictionaryStringInt64Array);
        using var res = await SendWithCsrfAsync(HttpMethod.Post, "https://presence.roblox.com/v1/presence/users", cookie, body, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new CookieExpiredException();
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var list = new List<PresenceInfo>();
        foreach (var p in doc.RootElement.GetProperty("userPresences").EnumerateArray())
        {
            list.Add(new PresenceInfo(
                p.GetProperty("userId").GetInt64(),
                p.TryGetProperty("userPresenceType", out var t) ? t.GetInt32() : 0,
                Str(p, "lastLocation"),
                p.TryGetProperty("placeId", out var pl) && pl.ValueKind == JsonValueKind.Number ? pl.GetInt64() : null,
                p.TryGetProperty("gameId", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null));
        }
        return list;
    }

    /// <summary>Link share baru (roblox.com/share?code=...&type=Server) -> placeId + linkCode server privat.</summary>
    public async Task<(long PlaceId, string LinkCode)> ResolveShareLinkAsync(string cookie, string code, CancellationToken ct = default)
    {
        var body = "{\"linkId\":" + JsonSerializer.Serialize(code, PresenceJson.Default.String) + ",\"linkType\":\"Server\"}";
        using var res = await SendWithCsrfAsync(HttpMethod.Post, "https://apis.roblox.com/sharelinks/v1/resolve-link", cookie, body, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("privateServerInviteData", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            long placeId = d.GetProperty("placeId").GetInt64();
            string link = Str(d, "linkCode");
            if (placeId > 0 && link.Length > 0) return (placeId, link);
        }
        throw new InvalidOperationException("Link share tidak berisi server privat yang valid.");
    }

    // ---------- Game ----------

    public async Task<List<GameRef>> SearchGamesAsync(string query, int max, CancellationToken ct = default)
    {
        var url = "https://apis.roblox.com/search-api/omni-search?searchQuery=" + Uri.EscapeDataString(query) +
                  "&sessionId=" + Guid.NewGuid() + "&pageType=all";
        using var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var list = new List<GameRef>();
        if (!doc.RootElement.TryGetProperty("searchResults", out var groups)) return list;
        foreach (var grp in groups.EnumerateArray())
        {
            if (!grp.TryGetProperty("contents", out var contents)) continue;
            foreach (var c in contents.EnumerateArray())
            {
                if (Str(c, "contentType") != "Game") continue;
                long placeId = c.TryGetProperty("rootPlaceId", out var rp) ? rp.GetInt64() : 0;
                if (placeId <= 0 || list.Any(g => g.PlaceId == placeId)) continue;
                list.Add(new GameRef
                {
                    PlaceId = placeId,
                    UniverseId = c.TryGetProperty("universeId", out var u) ? u.GetInt64() : 0,
                    Name = Str(c, "name"),
                    Creator = Str(c, "creatorName"),
                    Playing = c.TryGetProperty("playerCount", out var pc) && pc.ValueKind == JsonValueKind.Number ? pc.GetInt64() : 0,
                });
                if (list.Count >= max) return list;
            }
        }
        return list;
    }

    public async Task<GameRef?> ResolvePlaceAsync(long placeId, CancellationToken ct = default)
    {
        using var res = await _http.GetAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("universeId", out var uEl) || uEl.ValueKind != JsonValueKind.Number) return null;
        long universeId = uEl.GetInt64();
        var games = await GetGamesAsync(new[] { universeId }, ct);
        var g = games.FirstOrDefault();
        if (g == null) return null;
        g.PlaceId = placeId; // pakai place yang diminta (bisa sub-place)
        return g;
    }

    public async Task<List<GameRef>> GetGamesAsync(IEnumerable<long> universeIds, CancellationToken ct = default)
    {
        var ids = string.Join(",", universeIds.Distinct());
        using var res = await _http.GetAsync("https://games.roblox.com/v1/games?universeIds=" + ids, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var list = new List<GameRef>();
        foreach (var g in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            list.Add(new GameRef
            {
                UniverseId = g.GetProperty("id").GetInt64(),
                PlaceId = g.TryGetProperty("rootPlaceId", out var rp) ? rp.GetInt64() : 0,
                Name = Str(g, "name"),
                Creator = g.TryGetProperty("creator", out var cr) ? Str(cr, "name") : "",
                Playing = g.TryGetProperty("playing", out var pl) && pl.ValueKind == JsonValueKind.Number ? pl.GetInt64() : 0,
            });
        }
        return list;
    }

    public async Task<(List<ServerInfo> Servers, string? NextCursor)> GetServersAsync(long placeId, string? cursor, CancellationToken ct = default)
    {
        var url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder=Desc&limit=100&excludeFullGames=true";
        if (!string.IsNullOrEmpty(cursor)) url += "&cursor=" + Uri.EscapeDataString(cursor);
        using var res = await _http.GetAsync(url, ct);
        if (res.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Roblox membatasi permintaan (429). Tunggu sebentar lalu refresh.");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var list = new List<ServerInfo>();
        foreach (var s in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            list.Add(new ServerInfo(
                Str(s, "id"),
                s.TryGetProperty("playing", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0,
                s.TryGetProperty("maxPlayers", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : 0,
                s.TryGetProperty("ping", out var pg) && pg.ValueKind == JsonValueKind.Number ? pg.GetInt32() : 0));
        }
        string? next = doc.RootElement.TryGetProperty("nextPageCursor", out var nc) && nc.ValueKind == JsonValueKind.String ? nc.GetString() : null;
        return (list, next);
    }

    // ---------- Thumbnail ----------

    public Task<Dictionary<long, string>> GetGameIconsAsync(IEnumerable<long> universeIds, CancellationToken ct = default) =>
        GetThumbsAsync("https://thumbnails.roblox.com/v1/games/icons?size=150x150&format=Png&isCircular=false&universeIds=", universeIds, ct);

    public Task<Dictionary<long, string>> GetAvatarHeadshotsAsync(IEnumerable<long> userIds, CancellationToken ct = default) =>
        GetThumbsAsync("https://thumbnails.roblox.com/v1/users/avatar-headshot?size=150x150&format=Png&isCircular=false&userIds=", userIds, ct);

    private async Task<Dictionary<long, string>> GetThumbsAsync(string baseUrl, IEnumerable<long> ids, CancellationToken ct)
    {
        var result = new Dictionary<long, string>();
        var arr = ids.Where(i => i > 0).Distinct().ToArray();
        if (arr.Length == 0) return result;
        using var res = await _http.GetAsync(baseUrl + string.Join(",", arr), ct);
        if (!res.IsSuccessStatusCode) return result;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        foreach (var t in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var url = Str(t, "imageUrl");
            if (url.Length > 0) result[t.GetProperty("targetId").GetInt64()] = url;
        }
        return result;
    }

    public async Task<byte[]?> DownloadAsync(string url, CancellationToken ct = default)
    {
        try { return await _http.GetByteArrayAsync(url, ct); }
        catch { return null; }
    }

    // ---------- Region server ----------

    /// <summary>
    /// Minta info join (tanpa membuka Roblox) untuk tahu alamat IP server game.
    /// Hanya web API; tidak ada yang menyentuh proses Roblox.
    /// </summary>
    public async Task<string?> GetServerAddressAsync(string cookie, long placeId, string jobId, CancellationToken ct = default)
    {
        string body = "{\"placeId\":" + placeId + ",\"gameId\":\"" + jobId + "\",\"gameJoinAttemptId\":\"" + Guid.NewGuid() + "\",\"isTeleport\":false}";
        using var res = await SendWithCsrfAsync(HttpMethod.Post, "https://gamejoin.roblox.com/v1/join-game-instance", cookie, body, ct, "Roblox/WinInet");
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new CookieExpiredException();
        if ((int)res.StatusCode == 429) throw new HttpRequestException("rate limit", null, HttpStatusCode.TooManyRequests);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("joinScript", out var js) || js.ValueKind != JsonValueKind.Object) return null;
        if (js.TryGetProperty("UdmuxEndpoints", out var ud) && ud.ValueKind == JsonValueKind.Array && ud.GetArrayLength() > 0)
        {
            var a = Str(ud[0], "Address");
            if (a.Length > 0) return a;
        }
        var m = Str(js, "MachineAddress");
        return m.Length > 0 ? m : null;
    }

    /// <summary>IP -> "Kota, KODE" lewat ip-api.com (batch, maks 100 IP).</summary>
    public async Task<Dictionary<string, string>> GeolocateAsync(IEnumerable<string> ips, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>();
        var list = ips.Distinct().Take(100).ToList();
        if (list.Count == 0) return result;
        var body = "[" + string.Join(",", list.Select(i => "\"" + i + "\"")) + "]";
        using var req = new HttpRequestMessage(HttpMethod.Post, "http://ip-api.com/batch?fields=status,query,countryCode,city,regionName")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return result;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var ip = Str(e, "query");
            if (Str(e, "status") != "success") { result[ip] = "?"; continue; }
            var city = Str(e, "city");
            if (city.Length == 0) city = Str(e, "regionName");
            var cc = Str(e, "countryCode");
            result[ip] = city.Length > 0 ? $"{city}, {cc}" : cc;
        }
        return result;
    }

    // ---------- Helper ----------

    private async Task<HttpResponseMessage> SendWithCsrfAsync(HttpMethod method, string url, string cookie, string jsonBody, CancellationToken ct, string? userAgent = null)
    {
        string? csrf = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var req = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            AddCookie(req, cookie);
            if (userAgent != null) { req.Headers.UserAgent.Clear(); req.Headers.TryAddWithoutValidation("User-Agent", userAgent); }
            req.Headers.Referrer = new Uri("https://www.roblox.com/");
            req.Headers.TryAddWithoutValidation("Origin", "https://www.roblox.com");
            if (csrf != null) req.Headers.TryAddWithoutValidation("x-csrf-token", csrf);
            var res = await _http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.Forbidden && csrf == null &&
                res.Headers.TryGetValues("x-csrf-token", out var tokens))
            {
                csrf = tokens.FirstOrDefault();
                res.Dispose();
                req.Dispose();
                continue;
            }
            return res;
        }
        throw new InvalidOperationException("Roblox menolak token CSRF.");
    }

    private static void AddCookie(HttpRequestMessage req, string cookie) =>
        req.Headers.TryAddWithoutValidation("Cookie", ".ROBLOSECURITY=" + cookie);

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, long[]>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal partial class PresenceJson : System.Text.Json.Serialization.JsonSerializerContext { }
