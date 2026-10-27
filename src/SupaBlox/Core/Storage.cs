using System.Text.Json;

namespace SupaBlox.Core;

/// <summary>Data lokal di %LocalAppData%\SupaBlox (accounts.json, settings.json, error.log).</summary>
public static class Storage
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SupaBlox");

    private static string AccountsPath => Path.Combine(Dir, "accounts.json");
    private static string SettingsPath => Path.Combine(Dir, "settings.json");
    private static readonly object Gate = new();

    public static List<Account> LoadAccounts()
    {
        try
        {
            if (File.Exists(AccountsPath))
                return JsonSerializer.Deserialize(File.ReadAllText(AccountsPath), AppJson.Default.ListAccount) ?? new();
        }
        catch (Exception ex) { Log("LoadAccounts", ex); Backup(AccountsPath); }
        return new();
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), AppJson.Default.AppSettings) ?? new();
        }
        catch (Exception ex) { Log("LoadSettings", ex); Backup(SettingsPath); }
        return new();
    }

    public static void SaveAccounts(List<Account> accounts) =>
        WriteAtomic(AccountsPath, JsonSerializer.Serialize(accounts, AppJson.Default.ListAccount));

    public static void SaveSettings(AppSettings s) =>
        WriteAtomic(SettingsPath, JsonSerializer.Serialize(s, AppJson.Default.AppSettings));

    private static void WriteAtomic(string path, string json)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
    }

    private static void Backup(string path)
    {
        try { File.Copy(path, path + ".rusak-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
    }

    public static void Log(string where, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}: {ex}\n\n");
        }
        catch { }
    }
}
