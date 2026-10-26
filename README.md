# SupaBlox

Multi-instance Roblox untuk Windows (C# .NET 8), hasil reverse engineering mekanisme Astro Account Manager.

## Cara kerja
Roblox menolak jalan dobel lewat dua objek kernel bernama: `ROBLOX_singletonMutex` dan `ROBLOX_singletonEvent`.
SupaBlox membuat dan memegang keduanya di satu thread khusus yang hidup selama aplikasi terbuka, jadi Roblox yang dibuka setelahnya tidak menutup dirinya sendiri.

Mode **mutex-only**: SupaBlox tidak menyentuh proses Roblox sama sekali (tanpa DuplicateHandle, WriteProcessMemory, injeksi, DLL, atau FFlags).

## Pakai
1. Tutup semua Roblox.
2. Jalankan `SupaBlox.exe`, biarkan jendelanya terbuka.
3. Buka Roblox berkali-kali.
4. Tekan Enter di SupaBlox untuk mematikan.

## Build
```powershell
dotnet build src\SupaBlox -c Release
dotnet publish src\SupaBlox -c Release -o publish
```
