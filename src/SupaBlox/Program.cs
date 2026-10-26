using System.Diagnostics;

namespace SupaBlox;

internal static class Program
{
    private static int Main()
    {
        Console.Title = "SupaBlox - Multi Roblox";

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("SupaBlox hanya untuk Windows.");
            return 1;
        }

        // Cegah SupaBlox jalan dua kali.
        using var self = new Mutex(true, @"Local\SupaBlox_SelfMutex", out bool first);
        if (!first)
        {
            Console.WriteLine("SupaBlox sudah berjalan.");
            return 0;
        }

        int running = Process.GetProcessesByName("RobloxPlayerBeta").Length;
        if (running > 0)
        {
            Console.WriteLine($"[!] Ada {running} RobloxPlayerBeta.exe yang sudah berjalan.");
            Console.WriteLine("    Mode mutex-only butuh SupaBlox dijalankan SEBELUM Roblox pertama.");
            Console.WriteLine("    Tutup semua Roblox, lalu tekan Enter (atau ketik q untuk keluar).");
            if ((Console.ReadLine() ?? "").Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) return 0;
        }

        using var holder = new SingletonHolder();
        if (!holder.Start(TimeSpan.FromSeconds(5)))
        {
            Console.WriteLine($"[x] Gagal: {holder.Error ?? "objek singleton tidak bisa dipegang."}");
            return 2;
        }

        Console.WriteLine($"[ok] Mutex : {(holder.MutexOwned ? "dimiliki SupaBlox" : holder.MutexExisted ? "sudah ada (milik proses lain), handle dipegang" : "gagal")}");
        Console.WriteLine($"[ok] Event : {(holder.EventHeld ? (holder.EventExisted ? "sudah ada, handle dipegang" : "dibuat & dipegang") : "gagal")}");
        if (!holder.MutexOwned)
            Console.WriteLine("[!] Mutex tidak dimiliki SupaBlox. Multi-instance mungkin gagal; tutup semua Roblox lalu jalankan ulang.");

        Console.WriteLine();
        Console.WriteLine("Multi Roblox AKTIF. Buka Roblox sebanyak yang kamu mau.");
        Console.WriteLine("Biarkan jendela ini terbuka. Tekan Enter untuk mematikan.");
        Console.ReadLine();
        return 0;
    }
}
