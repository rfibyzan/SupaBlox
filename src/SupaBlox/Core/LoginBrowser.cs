using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SupaBlox.Core;

/// <summary>
/// Login Roblox lewat Microsoft Edge (atau Chrome) dengan profil sementara yang terisolasi.
/// SupaBlox membaca cookie .ROBLOSECURITY lewat DevTools Protocol lokal (127.0.0.1) setelah login,
/// lalu menutup browser dan menghapus profil sementara. Password tidak pernah dibaca SupaBlox.
/// </summary>
public static class LoginBrowser
{
    public static string? FindBrowser()
    {
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        {
            Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task<string?> CaptureCookieAsync(Action<string> status, CancellationToken ct)
    {
        var exe = FindBrowser() ?? throw new InvalidOperationException("Microsoft Edge / Chrome tidak ditemukan. Pakai tab Tempel cookie.");
        int port = FreePort();
        string profile = Path.Combine(Storage.Dir, "login-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            Arguments = $"--user-data-dir=\"{profile}\" --remote-debugging-port={port} --remote-allow-origins=http://127.0.0.1:{port} " +
                        "--no-first-run --no-default-browser-check --disable-extensions --disable-sync " +
                        "--window-size=520,760 --app=https://www.roblox.com/login"
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Browser gagal dibuka.");
        status("Silakan login di jendela Roblox yang terbuka…");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        ClientWebSocket? ws = null;
        int msgId = 0;
        try
        {
            var deadline = DateTime.UtcNow.AddMinutes(10);
            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                await Task.Delay(1500, ct);
                if (proc.HasExited && ws == null) return null; // ditutup user sebelum DevTools siap

                try
                {
                    if (ws == null || ws.State != WebSocketState.Open)
                    {
                        ws?.Dispose();
                        var ver = await http.GetStringAsync($"http://127.0.0.1:{port}/json/version", ct);
                        using var vdoc = JsonDocument.Parse(ver);
                        var wsUrl = vdoc.RootElement.GetProperty("webSocketDebuggerUrl").GetString()!;
                        ws = new ClientWebSocket();
                        await ws.ConnectAsync(new Uri(wsUrl), ct);
                    }

                    var reply = await CallAsync(ws, ++msgId, "Storage.getCookies", ct);
                    using var doc = JsonDocument.Parse(reply);
                    if (doc.RootElement.TryGetProperty("result", out var result) &&
                        result.TryGetProperty("cookies", out var cookies))
                    {
                        foreach (var c in cookies.EnumerateArray())
                        {
                            if (c.GetProperty("name").GetString() == ".ROBLOSECURITY" &&
                                (c.GetProperty("domain").GetString() ?? "").Contains("roblox.com"))
                            {
                                var value = c.GetProperty("value").GetString();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    status("Login terdeteksi, menutup browser…");
                                    try { await CallAsync(ws, ++msgId, "Browser.close", ct); } catch { }
                                    return value;
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // DevTools belum siap / browser ditutup.
                    if (proc.HasExited) return null;
                    ws?.Dispose();
                    ws = null;
                }
            }
            return null;
        }
        finally
        {
            ws?.Dispose();
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    try { Directory.Delete(profile, true); return; } catch { await Task.Delay(1000); }
                }
            });
        }
    }

    private static async Task<string> CallAsync(ClientWebSocket ws, int id, string method, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes("{\"id\":" + id + ",\"method\":\"" + method + "\"}");
        await ws.SendAsync(payload, WebSocketMessageType.Text, true, ct);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) throw new WebSocketException("closed");
                ms.Write(buffer, 0, r.Count);
            } while (!r.EndOfMessage);
            var text = Encoding.UTF8.GetString(ms.ToArray());
            if (text.Contains("\"id\":" + id + ",") || text.Contains("\"id\":" + id + "}")) return text;
        }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
